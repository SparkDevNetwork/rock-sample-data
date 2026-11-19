// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the step workflows in configuration/step-workflows.json (issue 18): each workflow type has its trigger on its
    /// step type, every step completed since the trigger was built started exactly one workflow, each one is open or complete as
    /// its step's date says, it went to the right person (the campus pastor of the step's campus, or a named staff member),
    /// an open one shows in that person's My Workflows list, and each Cast story shows.
    /// </summary>
    internal static class CastStepWorkflowExpectations
    {
        /// <summary>
        /// Returns how many expectations were checked and adds each failure.
        /// </summary>
        /// <param name="anchor">The load's install day, which the open window counts back from.</param>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, List<string> failures )
        {
            var config = source.StepWorkflows;
            if ( config == null || source.Steps == null )
            {
                return 0;
            }

            var checks = 0;
            var pastorByCampus = CampusPastorGuids( source );
            foreach ( var definition in config.Workflows )
            {
                var stepType = source.Steps.StepTypes.First( t => t.Key == definition.StepType );
                checks++;
                var trigger = db.Query( @"
SELECT [t].[Id], [t].[CreatedDateTime], [t].[TriggerType], [t].[TypeQualifier], [t].[WorkflowName], [t].[IsActive],
    CASE WHEN [t].[StepProgramId] = [st].[StepProgramId] THEN 1 ELSE 0 END AS [IsProgramSet],
    [wt].[IsActive] AS [TypeIsActive], [wt].[IsPersisted], [wt].[MaxWorkflowAgeDays], [wt].[CompletedWorkflowRetentionPeriod], [wt].[LogRetentionPeriod], [wt].[CategoryId]
FROM [StepWorkflowTrigger] AS [t]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [t].[StepTypeId]
INNER JOIN [WorkflowType] AS [wt] ON [wt].[Id] = [t].[WorkflowTypeId]
WHERE [wt].[Guid] = @Type AND [st].[Guid] = @StepType",
                    P( "@Type", definition.Guid ), P( "@StepType", stepType.Guid ) ).ToList();
                var expectedTrigger = definition.IsManual ? 1 : 2;
                if ( trigger.Count != 1 )
                {
                    failures.Add( $"{definition.Name}: expected one trigger on the {stepType.Name} step, Rock has {trigger.Count}" );
                    continue;
                }

                var t = trigger[0];
                if ( Convert.ToInt32( t["TriggerType"] ) != expectedTrigger || ( string ) t["TypeQualifier"] != "|" || ( string ) t["WorkflowName"] != definition.Name || !( bool ) t["IsActive"]
                    || Convert.ToInt32( t["IsProgramSet"] ) != 1 || t["TypeIsActive"] as bool? != true || !( bool ) t["IsPersisted"] || t["CategoryId"] == null
                    || t["MaxWorkflowAgeDays"] != null || t["CompletedWorkflowRetentionPeriod"] != null || t["LogRetentionPeriod"] != null )
                {
                    failures.Add( $"{definition.Name}: its trigger on the {stepType.Name} step or its workflow type is not as Rock's editors save it, active, persisted, and kept" );
                }

                var triggerId = ( int ) t["Id"];
                if ( definition.IsManual )
                {
                    checks++;
                    var launched = Convert.ToInt32( db.Scalar( "SELECT COUNT(*) FROM [StepWorkflow] WHERE [StepWorkflowTriggerId] = @Trigger", P( "@Trigger", triggerId ) ) );
                    if ( launched != 0 )
                    {
                        failures.Add( $"{definition.Name}: staff start it by hand, and Rock links no step to a hand-started workflow, but {launched} steps have one" );
                    }

                    continue;
                }

                checks++;
                var steps = Steps( db, stepType.Guid, triggerId, ( DateTime ) t["CreatedDateTime"], anchor.Date.AddDays( 1 ) );
                var openSince = anchor.Date.AddDays( -config.OpenDays );
                var problems = new List<string>();
                foreach ( var step in steps )
                {
                    var who = $"step {step["StepId"]} ({step["FullName"]}, {( DateTime ) step["CompletedDateTime"]:yyyy-MM-dd})";
                    if ( Convert.ToInt32( step["Links"] ) != 1 )
                    {
                        problems.Add( $"{who} started {step["Links"]} workflows" );
                        continue;
                    }

                    var expected = definition.Assignee.CampusLeader
                        ? ( step["CampusGuid"] is Guid campusGuid && pastorByCampus.TryGetValue( campusGuid, out var pastor ) ? guids.Rock( pastor ) : ( Guid? ) null )
                        : guids.Rock( definition.Assignee.Staff.Value );
                    if ( step["AssigneeGuid"] as Guid? != expected )
                    {
                        problems.Add( $"{who} went to {step["AssigneeName"] as string ?? "nobody"}" );
                    }

                    var isOpen = ( DateTime ) step["CompletedDateTime"] >= openSince;
                    if ( isOpen && ( ( string ) step["Status"] != "Active" || Convert.ToInt32( step["InMyWorkflows"] ) != 1 ) )
                    {
                        problems.Add( $"{who} should wait at its form in {step["AssigneeName"]}'s My Workflows, but it is {step["Status"]}" );
                    }
                    else if ( !isOpen && ( ( string ) step["Status"] != "Completed" || step["DoneByGuid"] as Guid? != expected ) )
                    {
                        problems.Add( $"{who} should be completed by its assignee, but it is {step["Status"]}" );
                    }
                }

                if ( problems.Count > 0 )
                {
                    failures.Add( $"{definition.Name}: {problems.Count} of {steps.Count} steps are wrong, such as {string.Join( "; ", problems.Take( 3 ) )}" );
                }

                foreach ( var cast in config.Cast.Where( c => c.Workflow == definition.Key ) )
                {
                    checks++;
                    var step = steps.LastOrDefault( s => ( Guid ) s["PersonGuid"] == guids.Rock( cast.Person ) );
                    if ( step == null )
                    {
                        failures.Add( $"{cast.PersonName}: expected a {definition.Name} from their {stepType.Name} step, Rock has none" );
                        continue;
                    }

                    var shows = ( string ) step["Status"] == "Active" ? "open" : "complete";
                    if ( shows != cast.Expect )
                    {
                        failures.Add( $"{cast.PersonName}: expected their {definition.Name} {cast.Expect}, Rock shows it {shows}" );
                        continue;
                    }

                    var values = db.Query( @"
SELECT [a].[Key], [av].[Value]
FROM [StepWorkflow] AS [sw]
INNER JOIN [AttributeValue] AS [av] ON [av].[EntityId] = [sw].[WorkflowId]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] AND [a].[EntityTypeQualifierColumn] = 'WorkflowTypeId'
INNER JOIN [Workflow] AS [w] ON [w].[Id] = [sw].[WorkflowId] AND [a].[EntityTypeQualifierValue] = CAST( [w].[WorkflowTypeId] AS NVARCHAR( 50 ) )
WHERE [sw].[StepId] = @StepId", P( "@StepId", step["StepId"] ) ).ToDictionary( r => ( string ) r["Key"], r => ( string ) r["Value"] );
                    foreach ( var (key, value) in cast.Values )
                    {
                        var isPerson = definition.Fields.FirstOrDefault( f => f.Key == key )?.Type == "person";
                        var actual = values.GetValueOrDefault( key );
                        var matches = isPerson
                            ? actual != null && Guid.TryParse( actual, out var aliasGuid ) && db.Scalar( "SELECT [p].[Guid] FROM [PersonAlias] AS [pa] INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId] WHERE [pa].[Guid] = @Alias", P( "@Alias", aliasGuid ) ) as Guid? == guids.Rock( Guid.Parse( value ) )
                            : actual == value;
                        if ( !matches )
                        {
                            failures.Add( $"{cast.PersonName}: expected their {definition.Name} to hold {key} {value}, Rock shows {actual ?? "nothing"}" );
                        }
                    }
                }
            }

            return checks;
        }

        /// <summary>
        /// Every step of the type completed from the trigger's making to the install day, with the workflow it started, who that went to, and whether it
        /// shows in their My Workflows list by Rock's own query.
        /// </summary>
        private static List<Dictionary<string, object>> Steps( RockDatabase db, Guid stepTypeGuid, int triggerId, DateTime since, DateTime until )
        {
            return db.Query( @"
SELECT [s].[Id] AS [StepId], [s].[CompletedDateTime], [c].[Guid] AS [CampusGuid], [p].[Guid] AS [PersonGuid], [p].[NickName] + ' ' + [p].[LastName] AS [FullName],
    ( SELECT COUNT(*) FROM [StepWorkflow] AS [x] WHERE [x].[StepId] = [s].[Id] AND [x].[StepWorkflowTriggerId] = @Trigger ) AS [Links],
    [w].[Status], [ap].[Guid] AS [AssigneeGuid], [ap].[NickName] + ' ' + [ap].[LastName] AS [AssigneeName], [dp].[Guid] AS [DoneByGuid],
    CASE WHEN EXISTS (
        SELECT 1
        FROM [WorkflowAction] AS [a]
        INNER JOIN [WorkflowActionType] AS [at] ON [at].[Id] = [a].[ActionTypeId]
        INNER JOIN [WorkflowActivity] AS [act] ON [act].[Id] = [a].[ActivityId]
        INNER JOIN [WorkflowActivityType] AS [actt] ON [actt].[Id] = [act].[ActivityTypeId]
        INNER JOIN [WorkflowType] AS [wt] ON [wt].[Id] = [actt].[WorkflowTypeId]
        INNER JOIN [PersonAlias] AS [apa] ON [apa].[Id] = [act].[AssignedPersonAliasId]
        WHERE [act].[WorkflowId] = [w].[Id] AND [at].[WorkflowFormId] IS NOT NULL AND [a].[CompletedDateTime] IS NULL
            AND [act].[ActivatedDateTime] IS NOT NULL AND [act].[CompletedDateTime] IS NULL AND [w].[ActivatedDateTime] IS NOT NULL AND [w].[CompletedDateTime] IS NULL
            AND [wt].[IsActive] = 1 AND ISNULL( [actt].[IsActive], 1 ) = 1 AND [wt].[CategoryId] IS NOT NULL AND [apa].[PersonId] = [ap].[Id] ) THEN 1 ELSE 0 END AS [InMyWorkflows]
FROM [Step] AS [s]
INNER JOIN [StepType] AS [st] ON [st].[Id] = [s].[StepTypeId]
INNER JOIN [StepStatus] AS [ss] ON [ss].[Id] = [s].[StepStatusId]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [s].[PersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
LEFT JOIN [Campus] AS [c] ON [c].[Id] = [s].[CampusId]
LEFT JOIN [StepWorkflow] AS [sw] ON [sw].[StepId] = [s].[Id] AND [sw].[StepWorkflowTriggerId] = @Trigger
LEFT JOIN [Workflow] AS [w] ON [w].[Id] = [sw].[WorkflowId]
LEFT JOIN [WorkflowActivity] AS [wa] ON [wa].[WorkflowId] = [w].[Id]
LEFT JOIN [PersonAlias] AS [aa] ON [aa].[Id] = [wa].[AssignedPersonAliasId]
LEFT JOIN [Person] AS [ap] ON [ap].[Id] = [aa].[PersonId]
LEFT JOIN [PersonAlias] AS [da] ON [da].[Id] = [w].[ModifiedByPersonAliasId]
LEFT JOIN [Person] AS [dp] ON [dp].[Id] = [da].[PersonId]
WHERE [st].[Guid] = @StepType AND [ss].[IsCompleteStatus] = 1 AND [s].[CompletedDateTime] >= @Since AND [s].[CompletedDateTime] < @Until
ORDER BY [s].[CompletedDateTime], [s].[Id]",
                P( "@StepType", stepTypeGuid ), P( "@Trigger", triggerId ), P( "@Since", since ), P( "@Until", until ) );
        }

        /// <summary>
        /// Each campus's pastor's person Guid, by campus Guid, from configuration/staff.json.
        /// </summary>
        private static Dictionary<Guid, Guid> CampusPastorGuids( SampleDataSource source )
        {
            var staff = source.Staff;
            if ( staff == null )
            {
                return new Dictionary<Guid, Guid>();
            }

            var members = staff.Staff.ToDictionary( s => s.Key, s => s.Person );
            return source.Campuses.Campuses
                .Where( c => staff.CampusPastors.ContainsKey( c.Key ) && members.ContainsKey( staff.CampusPastors[c.Key] ) )
                .ToDictionary( c => c.Guid, c => members[staff.CampusPastors[c.Key]] );
        }
    }
}
