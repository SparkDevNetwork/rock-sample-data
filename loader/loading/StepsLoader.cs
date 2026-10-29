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
        step's rule: an adult who attended, gave, or was in a small group in the last year. Student check-ins
        come from the attendance plan, the same check-ins the attendance step writes, so the Student Path is
        the same whether or not the Attendance part loads (decision 125).

        Reason: Steps and the records they come from always agree (decision 86).
    */

    /// <summary>
    /// Sets up the step programs and writes their steps, the Baptism Date and Baptized Here values, and the
    /// program completions.
    /// </summary>
    internal sealed class StepsLoader
    {
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );

        private static readonly Guid PersonStepHistoryCategoryGuid = new Guid( "517BB2D3-0A50-4132-818E-63BB3C81EAE9" );

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
        /// Sets up the programs and their step types, plans every step from what earlier steps wrote, and writes it.
        /// </summary>
        /// <param name="studentPath">The Student Path planned for this load, whose camp, trip, decision, and Student Leader steps are copied.</param>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, AttendanceLoader attendance, StudentPath studentPath, Dictionary<string, List<RockGift>> gifts, DateTime finalJourneyRun )
        {
            var config = _source.Steps;
            if ( config == null )
            {
                _db.Log.Write( "  Skipped (this package has no configuration/steps.json)" );
                return;
            }

            var setup = ConfigurePrograms( config );
            var facts = ReadFacts( config, people, castHouseholdKeys, attendance, gifts, finalJourneyRun );
            var plan = StepPlanner.Plan( config, facts, studentPath, _seed );
            Write( config, setup, plan );

            foreach ( var program in config.Programs )
            {
                var byType = string.Join( ", ", program.StepTypes.Select( t =>
                {
                    var steps = plan.Steps.Where( s => s.StepType == t.Key ).ToList();
                    var inProgress = steps.Count( s => s.Status == StepStatus.InProgress );
                    return $"{t.Name} {steps.Count:N0}{( inProgress > 0 ? $" ({inProgress:N0} in progress)" : string.Empty )}";
                } ) );
                _db.Log.Write( $"  {program.Name}: {byType}; program completions: {plan.Completions.Count( c => c.ProgramKey == program.Key ):N0}" );
            }

            var activeAdults = facts.People.Count( p => p.IsActiveAdult );
            var baptized = plan.Baptisms.Select( b => b.PersonId ).ToHashSet();
            string Share( bool isCast, string label )
            {
                var active = facts.People.Where( p => p.IsActiveAdult && p.IsCast == isCast ).ToList();
                return active.Count == 0 ? string.Empty : $", {100.0 * active.Count( p => baptized.Contains( p.PersonId ) ) / active.Count:N0}% of {label} active adults";
            }

            var students = facts.People.Count( p => studentPath.HasCheckedIn( p.PersonId ) );
            var studentBaptisms = facts.People.Count( p => !p.IsAdult && studentPath.HasCheckedIn( p.PersonId ) && baptized.Contains( p.PersonId ) );
            _db.Log.Write( $"  Active adults: {activeAdults:N0}; students: {students:N0}; baptisms: {plan.Baptisms.Count:N0} ({plan.Baptisms.Count( b => b.IsHere ):N0} here{Share( true, "Cast" )}{Share( false, "generated" )}; {studentBaptisms:N0} students)" );
            if ( plan.Shares.Count > 0 )
            {
                _db.Log.Write( $"  Shares (people who took the step of those who could): {string.Join( ", ", plan.Shares.Select( s => $"{s.Key} {s.Value.Chosen} of {s.Value.Candidates}" ) )}" );
            }
        }

        #region Programs and step types

        /// <summary>
        /// One program's Id and statuses in Rock, after setup.
        /// </summary>
        private sealed record ProgramSetup( int ProgramId, int InProgressStatusId, int CompleteStatusId );

        /// <summary>
        /// Every program and step type Id in Rock, after setup, by key.
        /// </summary>
        private sealed record StepsSetup( Dictionary<string, ProgramSetup> Programs, Dictionary<string, int> TypeIds, Dictionary<string, string> ProgramKeyByType );

        /// <summary>
        /// A new program's statuses, named and colored like the ones Rock gives the Discipleship Path, since a Rock
        /// status belongs to one program.
        /// </summary>
        private static readonly (string Name, string Color, bool IsComplete, int Order)[] NewProgramStatuses =
        {
            ("In Progress", "#FFC870", false, 1),
            ("Complete", "#16C98D", true, 2)
        };

        private StepsSetup ConfigurePrograms( StepsConfiguration config )
        {
            var setup = new StepsSetup( new Dictionary<string, ProgramSetup>(), new Dictionary<string, int>(), new Dictionary<string, string>() );
            for ( var order = 0; order < config.Programs.Count; order++ )
            {
                var program = config.Programs[order];
                setup.Programs[program.Key] = ConfigureProgram( config, program, order, setup.TypeIds );
                foreach ( var type in program.StepTypes )
                {
                    setup.ProgramKeyByType[type.Key] = program.Key;
                }
            }

            return setup;
        }

        /*
            10/21/26 - CLAUDE

            Rock's Discipleship Path and its four step types are kept and updated in place, because Rock code
            finds Baptism, Serve, and Small Group by Guid. Foundations, Give, and Lead are added. Rock's one
            prerequisite (Serve after Starting Point Class) is kept; the completion flow Linear (Preferred)
            shows and enforces it. The Student Path is a new program with its own statuses, filed in the same
            category as the Discipleship Path.

            Reason: This follows the serving pattern (decisions 68 and 83).
        */
        private ProgramSetup ConfigureProgram( StepsConfiguration config, StepProgramDefinition program, int programOrder, Dictionary<string, int> typeIds )
        {
            var now = DateTime.Now;
            var completionFlow = ( int ) ( CompletionFlow ) Enum.Parse( typeof( CompletionFlow ), program.CompletionFlow, true );
            int programId;
            if ( program.RockProgram )
            {
                programId = _db.RequireIdByGuid( "StepProgram", program.Guid );
                _db.Execute( "UPDATE [StepProgram] SET [CompletionFlow] = @CompletionFlow, [Description] = @Description, [ModifiedDateTime] = @Now WHERE [Id] = @Id",
                    P( "@CompletionFlow", completionFlow ), P( "@Description", program.Description ), P( "@Now", now ), P( "@Id", programId ) );
            }
            else
            {
                programId = AddProgram( program, programOrder, completionFlow, now );
            }

            for ( var order = 0; order < program.StepTypes.Count; order++ )
            {
                var type = program.StepTypes[order];
                var objectiveId = _db.RequireIdByGuid( "DefinedValue", config.Objectives[type.Objective] );
                var values = new[]
                {
                    P( "@Order", order ), P( "@EngagementType", ( int ) ( EngagementType ) Enum.Parse( typeof( EngagementType ), type.Engagement, true ) ), P( "@ImpactWeight", type.Impact ),
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

            foreach ( var prerequisite in program.Prerequisites )
            {
                _db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [StepTypePrerequisite] WHERE [StepTypeId] = @StepTypeId AND [PrerequisiteStepTypeId] = @PrerequisiteId )
    INSERT INTO [StepTypePrerequisite] ( [StepTypeId], [PrerequisiteStepTypeId], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
    VALUES ( @StepTypeId, @PrerequisiteId, 0, NEWID(), @Now, @Now, @ForeignKey )",
                    P( "@StepTypeId", typeIds[prerequisite.StepType] ), P( "@PrerequisiteId", typeIds[prerequisite.Requires] ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
            }

            return new ProgramSetup( programId, _db.RequireIdByGuid( "StepStatus", program.Statuses.InProgress ), _db.RequireIdByGuid( "StepStatus", program.Statuses.Complete ) );
        }

        /// <summary>
        /// Adds a new program, filed in its category, with its In Progress and Complete statuses. Returns its Id.
        /// </summary>
        private int AddProgram( StepProgramDefinition program, int order, int completionFlow, DateTime now )
        {
            var programId = ( int ) _db.Scalar( @"
INSERT INTO [StepProgram] ( [Name], [Description], [IconCssClass], [CategoryId], [DefaultListView], [IsActive], [Order], [StepTerm], [IsSystem], [CompletionFlow],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @IconCssClass, @CategoryId, 0, 1, @Order, 'Step', 0, @CompletionFlow, @Guid, @Now, @Now, @ForeignKey )",
                P( "@Name", program.Name ), P( "@Description", program.Description ), P( "@IconCssClass", program.IconCssClass ),
                P( "@CategoryId", program.CategoryGuid.HasValue ? _db.RequireIdByGuid( "Category", program.CategoryGuid.Value ) : ( object ) null ),
                P( "@Order", order ), P( "@CompletionFlow", completionFlow ), P( "@Guid", program.Guid ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );

            var guids = new[] { program.Statuses.InProgress, program.Statuses.Complete };
            for ( var i = 0; i < NewProgramStatuses.Length; i++ )
            {
                var status = NewProgramStatuses[i];
                _db.Execute( @"
INSERT INTO [StepStatus] ( [Name], [StepProgramId], [IsCompleteStatus], [StatusColor], [IsActive], [Order], [IsSystem], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @Name, @ProgramId, @IsComplete, @Color, 1, @Order, 0, @Guid, @Now, @Now, @ForeignKey )",
                    P( "@Name", status.Name ), P( "@ProgramId", programId ), P( "@IsComplete", status.IsComplete ), P( "@Color", status.Color ), P( "@Order", status.Order ),
                    P( "@Guid", guids[i] ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
            }

            return programId;
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

        private StepFacts ReadFacts( StepsConfiguration config, PeopleLoader people, HashSet<string> castHouseholdKeys, AttendanceLoader attendance, Dictionary<string, List<RockGift>> gifts, DateTime finalJourneyRun )
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

            foreach ( var (household, person) in StepPerson.Of( people.Households ) )
            {
                var isCast = castHouseholdKeys.Contains( household.Cast.Key );
                var attended = attendance.FamilySundays.TryGetValue( household.FamilyId, out var sundays ) && sundays.Any( s => s >= yearAgo );
                var gave = person.GivingId != null && gifts.TryGetValue( person.GivingId, out var unitGifts ) && unitGifts.Any( g => g.When >= yearAgo );
                var stepPerson = StepPerson.From( household, person, isCast );
                stepPerson.GivingId = person.GivingId;
                stepPerson.IsActiveAdult = person.IsAdult && person.CanAttend && person.BirthDate.HasValue && ( attended || gave || inSmallGroup.Contains( person.PersonId ) );
                stepPerson.FirstVisit = FirstVisit( household.Cast );
                stepPerson.CastBaptismDate = isCast ? _dates.Resolve( person.Cast.Attributes?.BaptismDate ) : null;
                facts.People.Add( stepPerson );
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
        private void Write( StepsConfiguration config, StepsSetup setup, StepPlan plan )
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
            var completionIds = new Dictionary<(int AliasId, string ProgramKey), int>();
            foreach ( var completion in plan.Completions )
            {
                var id = nextCompletionId++;
                completionIds[(completion.AliasId, completion.ProgramKey)] = id;

                // Rock's job runs the night the last step completes.
                var created = completion.End ?? completion.Start;
                completions.Rows.Add( id, setup.Programs[completion.ProgramKey].ProgramId, completion.AliasId, completion.CampusId, completion.Start, Value( completion.End ), DateKey( completion.Start ), Value( DateKey( completion.End ) ),
                    Guid.NewGuid(), _foreignKey, created, created );
            }

            var steps = TableFactory.Create( "Id:int", "StepTypeId:int", "StepStatusId:int", "PersonAliasId:int", "CampusId:int", "CompletedDateTime:date", "StartDateTime:date", "EndDateTime:date", "Order:int",
                "StartDateKey:int", "EndDateKey:int", "CompletedDateKey:int", "StepProgramCompletionId:int", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsDateTime:date", "ValueAsBoolean:bool", "IsPersistedValueDirty:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var baptisms = plan.Baptisms.ToDictionary( b => b.PersonId );
            var history = new StepHistory( _db, _foreignKey, config );

            foreach ( var step in plan.Steps.OrderBy( s => s.AliasId ).ThenBy( s => setup.TypeIds[s.StepType] ).ThenBy( s => s.Start ) )
            {
                var id = nextStepId++;
                history.Add( step, id );
                var modified = new[] { step.Start, step.Completed ?? step.Start, step.End ?? step.Start }.Max();
                var programKey = setup.ProgramKeyByType[step.StepType];
                var program = setup.Programs[programKey];
                steps.Rows.Add( id, setup.TypeIds[step.StepType], step.Status == StepStatus.Complete ? program.CompleteStatusId : program.InProgressStatusId, step.AliasId, step.CampusId,
                    Value( step.Completed ), step.Start, Value( step.End ), 0,
                    DateKey( step.Start ), Value( DateKey( step.End ) ), Value( DateKey( step.Completed ) ), completionIds.TryGetValue( (step.AliasId, programKey), out var completionId ) ? completionId : DBNull.Value,
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
            _db.BulkInsert( "History", history.Rows, keepIdentity: false );
        }

        /*
            10/23/26 - CLAUDE

            Rock's step save hook (Rock/Model/Engagement/Step/Step.SaveHook.cs) writes person history: one
            "Added" row when a step is made, with the campus as related data, and "Modify" rows when its status,
            end, or completion date changes later. A step the plan completes after its start (a class, or a
            Pending volunteer who became Active) was added In Progress and completed on that day. A step with an
            end date was ended on that day. Dates use Rock's US short date format, as the hook writes them.

            Reason: The person's History tab shows each step the way a church that recorded it in Rock sees it.
        */

        /// <summary>
        /// The person history rows Rock's step save hook writes for each step.
        /// </summary>
        private sealed class StepHistory
        {
            private readonly string _foreignKey;
            private readonly int _categoryId;
            private readonly int _personEntityTypeId;
            private readonly int _stepEntityTypeId;
            private readonly Dictionary<int, string> _campusNames;
            private readonly Dictionary<string, string> _typeNames;

            public DataTable Rows { get; } = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Verb:string", "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "RelatedData:string", "IsSensitive:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            public StepHistory( RockDatabase db, string foreignKey, StepsConfiguration config )
            {
                _foreignKey = foreignKey;
                _categoryId = db.RequireIdByGuid( "Category", PersonStepHistoryCategoryGuid );
                _personEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person'" ) );
                _stepEntityTypeId = Convert.ToInt32( db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Step'" ) );
                _campusNames = db.Query( "SELECT [Id], [Name] FROM [Campus]" ).ToDictionary( r => Convert.ToInt32( r["Id"] ), r => ( string ) r["Name"] );
                _typeNames = config.StepTypes.ToDictionary( t => t.Key, t => t.Name );
            }

            public void Add( PlannedStep step, int stepId )
            {
                var name = _typeNames[step.StepType];
                Row( step, stepId, name, "STEPADDED", name, null, null, _campusNames.GetValueOrDefault( step.CampusId ), step.Start );

                if ( step.Completed.HasValue && step.Completed.Value > step.Start )
                {
                    Row( step, stepId, name, "MODIFY", "Step Status", "In Progress", "Complete", null, step.Completed.Value );
                    Row( step, stepId, name, "MODIFY", "Completed", string.Empty, ShortDate( step.Completed.Value ), null, step.Completed.Value );
                }

                if ( step.End.HasValue && step.End.Value > step.Start )
                {
                    Row( step, stepId, name, "MODIFY", "Step End", string.Empty, ShortDate( step.End.Value ), null, step.End.Value );
                }
            }

            private void Row( PlannedStep step, int stepId, string caption, string verb, string valueName, string oldValue, string newValue, string relatedData, DateTime when )
            {
                Rows.Rows.Add( false, _categoryId, _personEntityTypeId, step.PersonId, caption, _stepEntityTypeId, stepId, verb, "Record", valueName,
                    ( object ) oldValue ?? DBNull.Value, ( object ) newValue ?? DBNull.Value, ( object ) relatedData ?? DBNull.Value, false,
                    Guid.NewGuid(), _foreignKey, when, when );
            }

            private static string ShortDate( DateTime date ) => date.ToString( "M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture );
        }

        private static int? DateKey( DateTime? date ) => date.HasValue ? int.Parse( date.Value.ToString( "yyyyMMdd" ) ) : null;

        private static int DateKey( DateTime date ) => int.Parse( date.ToString( "yyyyMMdd" ) );

        private static object Value<T>( T? value ) where T : struct => value.HasValue ? value.Value : DBNull.Value;

        #endregion
    }
}
