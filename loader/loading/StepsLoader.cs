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

using System.Data;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/21/26 - CLAUDE

        Group memberships are read back from the database, so each Small Group, Serve, and Lead step has
        exactly its membership's dates. Gifts come from the same in-memory list the giving analytics replayed,
        so each Give step agrees with the journey stage Rock shows. Who is an active adult uses the prayer
        step's rule: an adult who attended, gave, or was in a small group in the last year.

        Reason: Steps and the records they come from always agree (decision 86).
    */

    /// <summary>
    /// Sets up the adult step program and writes its steps, the Baptism Date and Baptized Here values, and
    /// the program completions.
    /// </summary>
    internal sealed class StepsLoader
    {
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly DateTime _historyStart;
        private readonly Dictionary<string, int> _groupTypeIds;

        public StepsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed, DateTime historyStart, int smallGroupTypeId, int servingGroupTypeId )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _historyStart = historyStart;
            _groupTypeIds = new Dictionary<string, int> { ["small-group"] = smallGroupTypeId, ["serving"] = servingGroupTypeId };
        }

        /// <summary>
        /// Sets up the program and its step types, plans every step from what earlier steps wrote, and writes it.
        /// </summary>
        public void Load( PeopleLoader people, IReadOnlySet<string> castHouseholdKeys, Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts, DateTime finalJourneyRun )
        {
            var config = _source.Steps;
            if ( config == null )
            {
                Console.WriteLine( "  Skipped (this package has no configuration/steps.json)" );
                return;
            }

            var program = ConfigureProgram( config );
            var facts = ReadFacts( people, castHouseholdKeys, familySundays, gifts, finalJourneyRun );
            var plan = StepPlanner.Plan( config, facts, _seed );
            Write( config, program, plan );

            var byType = string.Join( ", ", config.StepTypes.Select( t =>
            {
                var steps = plan.Steps.Where( s => s.StepType == t.Key ).ToList();
                var inProgress = steps.Count( s => s.Status == StepStatus.InProgress );
                return $"{t.Name} {steps.Count:N0}{( inProgress > 0 ? $" ({inProgress:N0} in progress)" : string.Empty )}";
            } ) );
            var activeAdults = facts.People.Count( p => p.IsActiveAdult );
            var baptized = plan.Baptisms.Select( b => b.PersonId ).ToHashSet();
            var generatedActive = facts.People.Where( p => p.IsActiveAdult && !p.IsCast ).ToList();
            var generatedShare = generatedActive.Count == 0 ? string.Empty : $", {100.0 * generatedActive.Count( p => baptized.Contains( p.PersonId ) ) / generatedActive.Count:N0}% of generated active adults";
            Console.WriteLine( $"  Steps: {plan.Steps.Count:N0} ({byType})" );
            Console.WriteLine( $"  Active adults: {activeAdults:N0}; baptisms: {plan.Baptisms.Count:N0} ({plan.Baptisms.Count( b => b.IsHere ):N0} here{generatedShare}); program completions: {plan.Completions.Count:N0}" );
        }

        #region Program and step types

        /// <summary>
        /// Rock's program, its step types, and their Ids, after setup.
        /// </summary>
        private sealed record ProgramSetup( int ProgramId, Dictionary<string, int> TypeIds, int InProgressStatusId, int CompleteStatusId );

        /*
            10/21/26 - CLAUDE

            Rock's Discipleship Path and its four step types are kept and updated in place, because Rock code
            finds Baptism, Serve, and Small Group by Guid. Foundations, Give, and Lead are added. Rock's one
            prerequisite (Serve after Starting Point Class) is kept; the completion flow Linear (Preferred)
            shows and enforces it.

            Reason: This follows the serving pattern (decisions 68 and 83).
        */
        private ProgramSetup ConfigureProgram( StepsConfiguration config )
        {
            var now = DateTime.Now;
            var programId = _db.RequireIdByGuid( "StepProgram", config.Program.Guid );
            _db.Execute( "UPDATE [StepProgram] SET [CompletionFlow] = @CompletionFlow, [Description] = @Description, [ModifiedDateTime] = @Now WHERE [Id] = @Id",
                P( "@CompletionFlow", ( int ) Enum.Parse<CompletionFlow>( config.Program.CompletionFlow, true ) ), P( "@Description", config.Program.Description ), P( "@Now", now ), P( "@Id", programId ) );

            var typeIds = new Dictionary<string, int>();
            for ( var order = 0; order < config.StepTypes.Count; order++ )
            {
                var type = config.StepTypes[order];
                var objectiveId = _db.RequireIdByGuid( "DefinedValue", config.Objectives[type.Objective] );
                var values = new[]
                {
                    P( "@Order", order ), P( "@EngagementType", ( int ) Enum.Parse<EngagementType>( type.Engagement, true ) ), P( "@ImpactWeight", type.Impact ),
                    P( "@ObjectiveId", objectiveId ), P( "@IconCssClass", type.IconCssClass ), P( "@HighlightColor", type.HighlightColor ),
                    P( "@AllowMultiple", type.AllowMultiple ), P( "@HasEndDate", type.HasEndDate ), P( "@Description", type.Description ),
                    P( "@CallToActionLabel", type.CallToActionLabel ), P( "@CallToActionDescription", type.CallToActionDescription ), P( "@Now", now )
                };

                if ( type.RockType )
                {
                    var id = _db.RequireIdByGuid( "StepType", type.Guid );
                    _db.Execute( @"
UPDATE [StepType]
SET [Order] = @Order, [EngagementType] = @EngagementType, [ImpactWeight] = @ImpactWeight, [OrganizationalObjectiveValueId] = @ObjectiveId,
    [IconCssClass] = @IconCssClass, [HighlightColor] = @HighlightColor, [AllowMultiple] = @AllowMultiple, [HasEndDate] = @HasEndDate,
    [Description] = COALESCE( @Description, [Description] ), [CallToActionLabel] = @CallToActionLabel, [CallToActionDescription] = @CallToActionDescription, [ModifiedDateTime] = @Now
WHERE [Id] = @Id", values.Append( P( "@Id", id ) ).ToArray() );
                    typeIds[type.Key] = id;
                }
                else
                {
                    typeIds[type.Key] = ( int ) _db.Scalar( @"
INSERT INTO [StepType] ( [StepProgramId], [Name], [Description], [IconCssClass], [AllowMultiple], [HasEndDate], [ShowCountOnBadge], [AllowManualEditing], [HighlightColor],
    [IsActive], [Order], [IsDateRequired], [IsSystem], [EngagementType], [ImpactWeight], [OrganizationalObjectiveValueId], [CallToActionLabel], [CallToActionDescription],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @ProgramId, @Name, @Description, @IconCssClass, @AllowMultiple, @HasEndDate, 0, 1, @HighlightColor,
    1, @Order, 1, 0, @EngagementType, @ImpactWeight, @ObjectiveId, @CallToActionLabel, @CallToActionDescription,
    @Guid, @Now, @Now, @ForeignKey )",
                        values.Concat( new[] { P( "@ProgramId", programId ), P( "@Name", type.Name ), P( "@Guid", type.Guid ), P( "@ForeignKey", _foreignKey ) } ).ToArray() );
                }
            }

            foreach ( var prerequisite in config.Prerequisites )
            {
                _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [StepTypePrerequisite] WHERE [StepTypeId] = @StepTypeId AND [PrerequisiteStepTypeId] = @PrerequisiteId )
    INSERT INTO [StepTypePrerequisite] ( [StepTypeId], [PrerequisiteStepTypeId], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
    VALUES ( @StepTypeId, @PrerequisiteId, 0, NEWID(), @Now, @Now, @ForeignKey )",
                    P( "@StepTypeId", typeIds[prerequisite.StepType] ), P( "@PrerequisiteId", typeIds[prerequisite.Requires] ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
            }

            return new ProgramSetup( programId, typeIds, _db.RequireIdByGuid( "StepStatus", config.Statuses.InProgress ), _db.RequireIdByGuid( "StepStatus", config.Statuses.Complete ) );
        }

        /// <summary>
        /// Rock's CompletionFlow values.
        /// </summary>
        private enum CompletionFlow
        {
            LinearRequired = 0,
            LinearPreferred = 1,
            NonLinear = 2
        }

        /// <summary>
        /// Rock's EngagementType values.
        /// </summary>
        private enum EngagementType
        {
            None = 0,
            Milestone = 1,
            Rhythm = 2
        }

        #endregion

        #region Facts

        private StepFacts ReadFacts( PeopleLoader people, IReadOnlySet<string> castHouseholdKeys, Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts, DateTime finalJourneyRun )
        {
            var yearAgo = _dates.Anchor.AddMonths( -12 );
            var facts = new StepFacts
            {
                Anchor = _dates.Anchor,
                HistoryStart = _historyStart,
                FinalJourneyRun = finalJourneyRun,
                Memberships = ReadMemberships(),
                GiftDatesByGivingId = gifts.ToDictionary( g => g.Key, g => g.Value.Select( gift => gift.When ).OrderBy( d => d ).ToList() ),
                OpenServeRequests = _db.Query( @"
SELECT DISTINCT [pa].[PersonId]
FROM [ConnectionRequest] AS [cr]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [cr].[PersonAliasId]
WHERE [cr].[ForeignKey] = @ForeignKey AND [cr].[ConnectionState] = 0", P( "@ForeignKey", _foreignKey ) ).Select( r => ( int ) r["PersonId"] ).ToHashSet()
            };

            var inSmallGroup = facts.Memberships.Where( m => m.GroupType == "small-group" && !m.IsPending && m.Left == null ).Select( m => m.PersonId ).ToHashSet();

            foreach ( var household in people.Households.Where( h => h.Cast.Kind != "business" ).OrderBy( h => h.Cast.Guid ) )
            {
                var isCast = castHouseholdKeys.Contains( household.Cast.Key );
                var firstVisit = FirstVisit( household.Cast );
                var attended = familySundays.TryGetValue( household.FamilyId, out var sundays ) && sundays.Any( s => s >= yearAgo );

                // A shared child is a member of two households; they belong to their primary family's.
                foreach ( var person in household.Members.Where( m => m.FamilyId == household.FamilyId && m.Cast.RecordType != "business" ) )
                {
                    var gave = person.GivingId != null && gifts.TryGetValue( person.GivingId, out var unitGifts ) && unitGifts.Any( g => g.When >= yearAgo );
                    facts.People.Add( new StepPerson
                    {
                        PersonId = person.PersonId,
                        AliasId = person.AliasId,
                        Guid = person.Cast.Guid,
                        Name = $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}",
                        CampusId = person.CampusId,
                        CampusKey = household.CampusKey,
                        GivingId = person.GivingId,
                        BirthDate = person.BirthDate,
                        IsAdult = person.IsAdult,
                        IsActiveAdult = person.IsAdult && person.CanAttend && person.BirthDate.HasValue && ( attended || gave || inSmallGroup.Contains( person.PersonId ) ),
                        IsCast = isCast,
                        FirstVisit = firstVisit,
                        CastBaptismDate = isCast ? _dates.Resolve( person.Cast.Attributes?.BaptismDate ) : null,
                        Classes = person.Cast.Steps ?? new Dictionary<string, CastStepClass>()
                    } );
                }
            }

            return facts;
        }

        /// <summary>
        /// The household's first visit: the earliest of when its attendance started and its members' First Visit
        /// values. Null when neither is known, which means the household came before the history window.
        /// </summary>
        private DateTime? FirstVisit( CastHousehold household )
        {
            var dates = household.Members.Select( m => _dates.Resolve( m.Attributes?.FirstVisit ) ).Append( _dates.Resolve( household.Attendance?.Started ) ).Where( d => d.HasValue ).ToList();
            return dates.Count == 0 ? null : dates.Min();
        }

        /// <summary>
        /// Every small group and serving team membership of this dataset, read back from what the earlier steps wrote.
        /// </summary>
        private List<MembershipFact> ReadMemberships()
        {
            var groupTypes = _groupTypeIds.ToDictionary( g => g.Value, g => g.Key );
            return _db.Query( @"
SELECT [gm].[PersonId], [gm].[GroupId], [g].[GroupTypeId], [gm].[GroupMemberStatus], [r].[IsLeader], [gm].[DateTimeAdded],
    CASE WHEN [gm].[GroupMemberStatus] = 0 OR [gm].[IsArchived] = 1 THEN ISNULL( [gm].[InactiveDateTime], [gm].[ArchivedDateTime] ) END AS [MemberEnded],
    CASE WHEN [g].[IsActive] = 0 OR [g].[IsArchived] = 1 THEN ISNULL( [g].[InactiveDateTime], [g].[ArchivedDateTime] ) END AS [GroupEnded]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
WHERE [gm].[ForeignKey] = @ForeignKey AND [g].[GroupTypeId] IN ( @SmallGroupTypeId, @ServingTypeId )
ORDER BY [gm].[Id]",
                P( "@ForeignKey", _foreignKey ), P( "@SmallGroupTypeId", _groupTypeIds["small-group"] ), P( "@ServingTypeId", _groupTypeIds["serving"] ) )
                .Select( r =>
                {
                    var memberEnded = r["MemberEnded"] as DateTime?;
                    var groupEnded = r["GroupEnded"] as DateTime?;
                    var left = memberEnded.HasValue && groupEnded.HasValue ? ( memberEnded < groupEnded ? memberEnded : groupEnded ) : memberEnded ?? groupEnded;
                    return new MembershipFact( ( int ) r["PersonId"], ( int ) r["GroupId"], groupTypes[( int ) r["GroupTypeId"]], Convert.ToBoolean( r["IsLeader"] ),
                        Convert.ToInt32( r["GroupMemberStatus"] ) == 2, ( DateTime ) r["DateTimeAdded"], left );
                } )
                .ToList();
        }

        #endregion

        #region Writing

        /// <summary>
        /// Bulk-copies the program completions, the steps (pointing at their completion), and the attribute values.
        /// </summary>
        private void Write( StepsConfiguration config, ProgramSetup program, StepPlan plan )
        {
            var groupEntityTypeId = _db.RequireIdByGuid( "EntityType", GroupEntityTypeGuid );
            var baptism = config.StepTypes.Single( t => t.Source.Kind == "baptism" ).Source;
            var baptismDateAttributeId = _db.RequireIdByGuid( "Attribute", baptism.BaptismDateAttributeGuid );
            var stepHereAttributeId = _db.RequireIdByGuid( "Attribute", baptism.BaptizedHereStepAttributeGuid );
            var personHereAttributeId = _db.RequireIdByGuid( "Attribute", baptism.BaptizedHerePersonAttributeGuid );
            var baptismTypeKey = config.StepTypes.Single( t => t.Source.Kind == "baptism" ).Key;

            var nextCompletionId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [StepProgramCompletion]" ) );
            var nextStepId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Step]" ) );

            var completions = TableFactory.Create( "Id:int", "StepProgramId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int", "EndDateKey:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var completionIds = new Dictionary<int, int>();
            foreach ( var completion in plan.Completions )
            {
                var id = nextCompletionId++;
                completionIds[completion.AliasId] = id;

                // Rock's job runs the night the last step completes.
                var created = completion.End ?? completion.Start;
                completions.Rows.Add( id, program.ProgramId, completion.AliasId, completion.CampusId, completion.Start, Value( completion.End ), DateKey( completion.Start ), Value( DateKey( completion.End ) ),
                    Guid.NewGuid(), _foreignKey, created, created );
            }

            var steps = TableFactory.Create( "Id:int", "StepTypeId:int", "StepStatusId:int", "PersonAliasId:int", "CampusId:int", "CompletedDateTime:date", "StartDateTime:date", "EndDateTime:date", "Order:int",
                "StartDateKey:int", "EndDateKey:int", "CompletedDateKey:int", "StepProgramCompletionId:int", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsDateTime:date", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var baptisms = plan.Baptisms.ToDictionary( b => b.PersonId );

            foreach ( var step in plan.Steps.OrderBy( s => s.AliasId ).ThenBy( s => program.TypeIds[s.StepType] ).ThenBy( s => s.Start ) )
            {
                var id = nextStepId++;
                var modified = new[] { step.Start, step.Completed ?? step.Start, step.End ?? step.Start }.Max();
                steps.Rows.Add( id, program.TypeIds[step.StepType], step.Status == StepStatus.Complete ? program.CompleteStatusId : program.InProgressStatusId, step.AliasId, step.CampusId,
                    Value( step.Completed ), step.Start, Value( step.End ), 0,
                    DateKey( step.Start ), Value( DateKey( step.End ) ), Value( DateKey( step.Completed ) ), completionIds.TryGetValue( step.AliasId, out var completionId ) ? completionId : DBNull.Value,
                    step.RelatedGroupId.HasValue ? groupEntityTypeId : DBNull.Value, Value( step.RelatedGroupId ),
                    Guid.NewGuid(), _foreignKey, step.Start, modified );

                if ( step.StepType == baptismTypeKey )
                {
                    var here = baptisms[step.PersonId].IsHere;
                    values.Rows.Add( false, stepHereAttributeId, id, here ? "True" : "False", DBNull.Value, here, true, Guid.NewGuid(), _foreignKey, step.Start, step.Start );
                }
            }

            // The Baptism Date and Baptized Here person attributes agree with the Baptism step.
            foreach ( var b in plan.Baptisms )
            {
                values.Rows.Add( false, baptismDateAttributeId, b.PersonId, b.Date.ToString( "o" ), b.Date, DBNull.Value, true, Guid.NewGuid(), _foreignKey, b.Date, b.Date );
                values.Rows.Add( false, personHereAttributeId, b.PersonId, b.IsHere ? "True" : "False", DBNull.Value, b.IsHere, true, Guid.NewGuid(), _foreignKey, b.Date, b.Date );
            }

            _db.BulkInsert( "StepProgramCompletion", completions, keepIdentity: true );
            _db.BulkInsert( "Step", steps, keepIdentity: true );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        private static int? DateKey( DateTime? date ) => date.HasValue ? int.Parse( date.Value.ToString( "yyyyMMdd" ) ) : null;

        private static int DateKey( DateTime date ) => int.Parse( date.ToString( "yyyyMMdd" ) );

        private static object Value<T>( T? value ) where T : struct => value.HasValue ? value.Value : DBNull.Value;

        #endregion
    }
}
