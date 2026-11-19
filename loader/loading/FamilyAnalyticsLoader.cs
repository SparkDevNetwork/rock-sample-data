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

using System.Globalization;

using RockSampleData.Loader.Database;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Fills eRA as if Rock's Family Analytics job had run every Tuesday at 8 PM through the history window, then
    /// runs Rock's own giving and attendance count procedures, and turns the job on (decisions D-era-1 to D-era-4).
    /// </summary>
    /// <remarks>
    /// It reads the families, the gifts, and the weekend service attendance from the rows the earlier steps wrote,
    /// so it always runs, and it shows whatever parts loaded, as Rock's job would.
    /// </remarks>
    internal sealed class FamilyAnalyticsLoader
    {
        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );
        private static readonly Guid AdultRoleGuid = new Guid( "2639F9A5-2AAE-4E48-A8C3-4FFE86681E42" );
        private static readonly Guid PersonRecordTypeGuid = new Guid( "36CF10D6-C695-413D-8E7C-4546EFEF385E" );
        private static readonly Guid InactiveRecordStatusGuid = new Guid( "1DAD99D5-41A9-4865-8366-F269902B80A4" );
        private static readonly Guid ContributionGuid = new Guid( "2D607262-52D6-4724-910D-5C6E8FB89ACC" );

        private static readonly Guid CurrentlyAnEraGuid = new Guid( "CE5739C5-2156-E2AB-48E5-1337C38B935E" );
        private static readonly Guid EraStartDateGuid = new Guid( "A106610C-A7A1-469E-4097-9DE6400FDFC2" );
        private static readonly Guid EraEndDateGuid = new Guid( "4711D67E-7526-9582-4A8E-1CD7BBE1B3A2" );

        /// <summary>
        /// The person attributes Rock's giving and attendance count procedures write: First Gave, Last Gave, Times Given
        /// (6 and 52 weeks), First Checked-In, Last Checked-In, and Times Checked-In (16 weeks).
        /// </summary>
        private static readonly Guid[] CountAttributeGuids =
        {
            new Guid( "EE5EC76A-D4B9-56B5-4B48-29627D945F10" ),
            new Guid( "02F64263-E290-399E-4487-FC236F4DE81F" ),
            new Guid( "AC11EF53-AE55-79A0-4CAD-43721750E988" ),
            new Guid( "57700E8F-ED11-D787-415A-04DDF411BB10" ),
            new Guid( "AB12B3B0-55B8-D6A5-4C1F-DB9CCB2C4342" ),
            new Guid( "5F4C6462-018E-D19C-4AB0-9843CB21C57E" ),
            new Guid( "45A1E978-DC5B-CFA1-4AF4-EA098A24C914" )
        };

        private static readonly Guid EraStepTypeGuid = new Guid( "E57468BE-15BF-48B6-AAB2-F8E2B02720F3" );
        private static readonly Guid InProgressStatusGuid = new Guid( "8013C752-31AA-46C6-9B55-BCFBE57C0577" );
        private static readonly Guid CompleteStatusGuid = new Guid( "359D3CE0-E144-491E-8C3B-2A2BCE55C04B" );
        private static readonly Guid PersonAnalyticsHistoryCategoryGuid = new Guid( "C1524D2E-3E8F-3D83-45F8-526B749D79F0" );

        private static readonly Guid FamilyAnalyticsJobGuid = new Guid( "623F4751-C654-FEB7-45B7-59685B1F60AE" );
        private static readonly Guid BooleanFieldTypeGuid = new Guid( "1EDAFDED-DFE6-4334-B019-6EECBA89E05A" );
        private const string JobClass = "Rock.Jobs.CalculateFamilyAnalytics";

        /// <summary>
        /// The source of change Rock's job records on its history lines.
        /// </summary>
        private const string SourceOfChange = "Calculate Family Analytics Job";

        public const string EraProcedure = "spCrm_FamilyAnalyticsEraDataset";
        public const string GivingProcedure = "spCrm_FamilyAnalyticsGiving";
        public const string AttendanceProcedure = "spCrm_FamilyAnalyticsAttendance";

        private readonly RockDatabase _db;
        private readonly AnchorDates _dates;
        private readonly DateTime _historyStart;
        private readonly string _foreignKey;

        public FamilyAnalyticsLoader( RockDatabase db, AnchorDates dates, DateTime historyStart, string foreignKey )
        {
            _db = db;
            _dates = dates;
            _historyStart = historyStart;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// True to check, after the load, that Rock's own eRA procedure run as of install day finds nothing to change.
        /// The CLI and the verify tool set it with the Cast expectations; Rock's block skips it (decision 217).
        /// </summary>
        public bool IsChecked { get; set; }

        /// <summary>
        /// The attributes whose values Rock's count procedures wrote, which Rock's save hook never touches, so the
        /// load's run of that hook leaves them as the procedures wrote them.
        /// </summary>
        public IReadOnlyCollection<int> ProcedureAttributeIds { get; private set; } = Array.Empty<int>();

        /*
            11/19/26 - CLAUDE

            Rock's job runs on Tuesdays at 8 PM. Its windows end on the previous Sunday as of the run, which moves
            on Monday. So a load anchored to a Monday, or to a Tuesday before 8 PM, would hand the job a Sunday its
            last Tuesday run never saw, and its first run after install would move families. The procedure also
            gives the flag to a newly entered family's inactive members only on the run after. So the replay ends
            with one more run at the start of install day, which settles both. On most days it changes nothing.

            Reason: The job's first run after install finds nothing to change (decision D-era-2).
        */

        /// <summary>
        /// The job's runs: every Tuesday at 8 PM from the first on or after the start of the history window and before
        /// install day, then one at the start of install day.
        /// </summary>
        public static List<DateTime> RunTimes( DateTime historyStart, DateTime anchor )
        {
            var runs = new List<DateTime>();
            var run = historyStart.Date.AddHours( 20 );
            while ( run.DayOfWeek != DayOfWeek.Tuesday )
            {
                run = run.AddDays( 1 );
            }

            for ( ; run < anchor.Date; run = run.AddDays( 7 ) )
            {
                runs.Add( run );
            }

            runs.Add( anchor.Date );
            return runs;
        }

        /// <summary>
        /// Replays the job, writes eRA, runs Rock's count procedures, and turns the job on.
        /// </summary>
        public void Load()
        {
            EraChurch church;
            using ( _db.Log.TimePart( "Read families, gifts, and weekend attendance" ) )
            {
                church = Read();
            }

            var runs = RunTimes( _historyStart, _dates.Anchor );
            var entries = 0;
            var exits = 0;
            using ( _db.Log.TimePart( "Replay the weekly runs" ) )
            {
                foreach ( var run in runs )
                {
                    var results = RockFamilyAnalytics.Run( church, run );
                    entries += results.Count( r => !r.IsEra );
                    exits += results.Count( r => r.IsEra && RockFamilyAnalytics.MeetsExit( r ) );
                }
            }

            var finalRun = runs[runs.Count - 1];
            int steps, history;
            using ( _db.Log.TimePart( "Write eRA" ) )
            {
                (steps, history) = Write( church );
                TurnOnJob();
            }

            using ( _db.Log.TimePart( "Rock's count procedures" ) )
            {
                RunCountProcedures( finalRun );
            }

            var eraPeople = church.People.Values.Count( p => p.Flag != null );
            var eraFamilies = church.Families.Values.Count( f => f.Members.Any( m => m.IsAdult && church.People[m.PersonId].Flag != null ) );
            _db.Log.Write( $"  eRA: {eraFamilies:N0} families ({eraPeople:N0} people) on install day, after {entries:N0} entries and {exits:N0} exits in {runs.Count:N0} weekly runs; {steps:N0} eRA steps and {history:N0} history lines" );

            if ( IsChecked )
            {
                using ( _db.Log.TimePart( "Check with Rock's eRA procedure" ) )
                {
                    CheckWithRock( finalRun );
                }
            }
        }

        /// <summary>
        /// Everything the job reads: families and their members, people, each giving id's gift Sundays, and each family's attended Sundays.
        /// </summary>
        private EraChurch Read()
        {
            var church = new EraChurch();
            var familyTypeId = _db.RequireIdByGuid( "GroupType", FamilyGroupTypeGuid );
            var adultRoleId = _db.RequireIdByGuid( "GroupTypeRole", AdultRoleGuid );
            var personRecordTypeId = _db.RequireIdByGuid( "DefinedValue", PersonRecordTypeGuid );
            var inactiveStatusId = _db.RequireIdByGuid( "DefinedValue", InactiveRecordStatusGuid );

            foreach ( var row in _db.Query( "SELECT [Id], [CampusId], [IsArchived] FROM [Group] WHERE [GroupTypeId] = @FamilyTypeId", P( "@FamilyTypeId", familyTypeId ) ) )
            {
                var family = new EraFamily { Id = ( int ) row["Id"], CampusId = row["CampusId"] as int?, IsArchived = ( bool ) row["IsArchived"] };
                church.Families[family.Id] = family;
            }

            foreach ( var row in _db.Query( @"
SELECT [gm].[GroupId], [gm].[PersonId], [gm].[GroupRoleId], [gm].[IsArchived]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [g].[GroupTypeId] = @FamilyTypeId
ORDER BY [gm].[GroupId], [gm].[Id]", P( "@FamilyTypeId", familyTypeId ) ) )
            {
                church.Families[( int ) row["GroupId"]].Members.Add( new EraMember( ( int ) row["PersonId"], ( int ) row["GroupRoleId"] == adultRoleId, ( bool ) row["IsArchived"] ) );
            }

            foreach ( var row in _db.Query( "SELECT [Id], [PrimaryFamilyId], [PrimaryAliasId], [GivingId], [RecordTypeValueId], [RecordStatusValueId], [IsDeceased] FROM [Person]" ) )
            {
                var person = new EraPerson
                {
                    Id = ( int ) row["Id"],
                    PrimaryFamilyId = row["PrimaryFamilyId"] as int?,
                    PrimaryAliasId = row["PrimaryAliasId"] as int?,
                    GivingId = row["GivingId"] as string,
                    IsPersonRecord = row["RecordTypeValueId"] as int? == personRecordTypeId,
                    IsInactive = row["RecordStatusValueId"] as int? == inactiveStatusId,
                    IsDeceased = ( bool ) row["IsDeceased"]
                };
                church.People[person.Id] = person;
            }

            // Each contribution with a tax-deductible line counts once, refunds included, by the giver's giving id.
            foreach ( var giver in _db.Query( @"
SELECT [p].[GivingId], [ft].[SundayDate]
FROM [FinancialTransaction] AS [ft]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [ft].[AuthorizedPersonAliasId]
INNER JOIN [Person] AS [p] ON [p].[Id] = [pa].[PersonId]
WHERE [ft].[TransactionTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @Contribution )
    AND [ft].[SundayDate] IS NOT NULL AND [p].[GivingId] IS NOT NULL
    AND EXISTS ( SELECT 1 FROM [FinancialTransactionDetail] AS [d] INNER JOIN [FinancialAccount] AS [a] ON [a].[Id] = [d].[AccountId] WHERE [d].[TransactionId] = [ft].[Id] AND [a].[IsTaxDeductible] = 1 )",
                P( "@Contribution", ContributionGuid ) ).GroupBy( r => ( string ) r["GivingId"], StringComparer.OrdinalIgnoreCase ) )
            {
                church.GiftSundaysByGivingId[giver.Key] = giver.Select( r => ( DateTime ) r["SundayDate"] ).OrderBy( d => d ).ToArray();
            }

            // A family attended on a Sunday when any member in it attended a group whose type counts as a weekend service.
            foreach ( var family in _db.Query( @"
SELECT DISTINCT [fg].[Id] AS [FamilyId], [o].[SundayDate]
FROM [Attendance] AS [a]
INNER JOIN [AttendanceOccurrence] AS [o] ON [o].[Id] = [a].[OccurrenceId]
INNER JOIN [Group] AS [ag] ON [ag].[Id] = [o].[GroupId]
INNER JOIN [GroupType] AS [agt] ON [agt].[Id] = [ag].[GroupTypeId] AND [agt].[AttendanceCountsAsWeekendService] = 1
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
INNER JOIN [GroupMember] AS [fgm] ON [fgm].[PersonId] = [pa].[PersonId]
INNER JOIN [Group] AS [fg] ON [fg].[Id] = [fgm].[GroupId] AND [fg].[GroupTypeId] = @FamilyTypeId
WHERE [a].[DidAttend] = 1 AND [fgm].[IsArchived] = 0 AND [fg].[IsArchived] = 0", P( "@FamilyTypeId", familyTypeId ) ).GroupBy( r => ( int ) r["FamilyId"] ) )
            {
                church.AttendedSundaysByFamily[family.Key] = family.Select( r => ( DateTime ) r["SundayDate"] ).OrderBy( d => d ).ToArray();
            }

            return church;
        }

        /// <summary>
        /// Writes each person's eRA attribute values, eRA steps with their history and Core Steps completion, and
        /// eRA history lines. Returns how many steps and eRA history lines it wrote.
        /// </summary>
        private (int Steps, int History) Write( EraChurch church )
        {
            var flagId = _db.RequireIdByGuid( "Attribute", CurrentlyAnEraGuid );
            var startId = _db.RequireIdByGuid( "Attribute", EraStartDateGuid );
            var endId = _db.RequireIdByGuid( "Attribute", EraEndDateGuid );
            var stepTypeId = _db.RequireIdByGuid( "StepType", EraStepTypeGuid );
            var programId = Convert.ToInt32( _db.Scalar( "SELECT [StepProgramId] FROM [StepType] WHERE [Id] = @Id", P( "@Id", stepTypeId ) ) );
            var stepTypeName = ( string ) _db.Scalar( "SELECT [Name] FROM [StepType] WHERE [Id] = @Id", P( "@Id", stepTypeId ) );
            var inProgressId = _db.RequireIdByGuid( "StepStatus", InProgressStatusGuid );
            var completeId = _db.RequireIdByGuid( "StepStatus", CompleteStatusGuid );
            var historyCategoryId = _db.RequireIdByGuid( "Category", PersonAnalyticsHistoryCategoryGuid );
            var personEntityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Person'" ) );
            var attributeEntityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Attribute'" ) );

            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "ValueAsNumeric:decimal", "ValueAsDateTime:date", "ValueAsBoolean:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            void AddValue( int attributeId, int personId, EraValue value )
            {
                if ( value == null )
                {
                    return;
                }

                var (numeric, dateTime, boolean) = AttributeValueHook.ValueAs( value.Value, value.Modified.Year );
                values.Rows.Add( false, attributeId, personId, value.Value, ( object ) numeric ?? DBNull.Value, ( object ) ( value.AsDateTime ?? dateTime ) ?? DBNull.Value, ( object ) boolean ?? DBNull.Value,
                    Guid.NewGuid(), _foreignKey, value.Created, value.Modified );
            }

            var nextStepId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Step]" ) );
            var nextCompletionId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [StepProgramCompletion]" ) );
            var steps = TableFactory.Create( "Id:int", "StepTypeId:int", "StepStatusId:int", "PersonAliasId:int", "CampusId:int", "CompletedDateTime:date", "StartDateTime:date", "EndDateTime:date", "Order:int",
                "StartDateKey:int", "EndDateKey:int", "CompletedDateKey:int", "StepProgramCompletionId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var completions = TableFactory.Create( "Id:int", "StepProgramId:int", "PersonAliasId:int", "CampusId:int", "StartDateTime:date", "EndDateTime:date", "StartDateKey:int", "EndDateKey:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var stepHistory = new StepHistoryRows( _db, _foreignKey ) { SourceOfChange = SourceOfChange };
            var history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
                "Verb:string", "ChangeType:string", "ValueName:string", "NewValue:string", "IsSensitive:bool", "SourceOfChange:string", "CreatedByPersonAliasId:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var person in church.People.Values.OrderBy( p => p.Id ) )
            {
                AddValue( flagId, person.Id, person.Flag );
                AddValue( startId, person.Id, person.Start );
                AddValue( endId, person.Id, person.End );

                foreach ( var line in person.History )
                {
                    history.Rows.Add( false, historyCategoryId, personEntityTypeId, person.Id, "eRA", attributeEntityTypeId, flagId,
                        line.IsEntry ? "ENTERED" : "EXITED", "Attribute", line.IsEntry ? DBNull.Value : "eRA", line.IsEntry ? DBNull.Value : "Exited", false, SourceOfChange,
                        ( object ) person.PrimaryAliasId ?? DBNull.Value, Guid.NewGuid(), _foreignKey, line.At, line.At );
                }

                if ( person.Steps.Count == 0 )
                {
                    continue;
                }

                // A person's first exit makes their Core Steps completion (Rock's step save hook), and Rock's nightly Update
                // Step Program Completions job points each later eRA step of theirs at it.
                int? completionId = null;
                if ( person.CompletionStep is { } first )
                {
                    completionId = nextCompletionId++;
                    completions.Rows.Add( completionId, programId, person.PrimaryAliasId, ( object ) first.CampusId ?? DBNull.Value, first.Start, first.Completed, DateKey( first.Start ), DateKey( first.Completed.Value ),
                        Guid.NewGuid(), _foreignKey, first.Completed, first.Completed );
                }

                foreach ( var step in person.Steps )
                {
                    var stepId = nextStepId++;
                    var completed = ( object ) step.Completed ?? DBNull.Value;
                    steps.Rows.Add( stepId, stepTypeId, step.Completed.HasValue ? completeId : inProgressId, person.PrimaryAliasId, ( object ) step.CampusId ?? DBNull.Value, completed, step.Start, completed, 0,
                        DateKey( step.Start ), step.Completed.HasValue ? DateKey( step.Completed.Value ) : DBNull.Value, step.Completed.HasValue ? DateKey( step.Completed.Value ) : DBNull.Value,
                        ( object ) completionId ?? DBNull.Value, Guid.NewGuid(), _foreignKey, step.Start, step.Completed ?? step.Start );
                    stepHistory.Add( person.Id, stepId, stepTypeName, step.CampusId, step.Start, step.Completed, step.Completed );
                }
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            _db.BulkInsert( "StepProgramCompletion", completions, keepIdentity: true );
            _db.BulkInsert( "Step", steps, keepIdentity: true );
            _db.BulkInsert( "History", stepHistory.Rows, keepIdentity: false );
            _db.BulkInsert( "History", history, keepIdentity: false );
            return (steps.Rows.Count, history.Rows.Count);
        }

        /// <summary>
        /// Turns the job on (it ships off) with Set Visit Dates off, so its first run changes nothing: the visit dates wait
        /// for realistic record dates (decision D-era-3).
        /// </summary>
        private void TurnOnJob()
        {
            var jobId = _db.IdByGuid( "ServiceJob", FamilyAnalyticsJobGuid );
            if ( !jobId.HasValue )
            {
                _db.Log.Write( "  Rock's Family Analytics job is missing, so it stays as it is" );
                return;
            }

            _db.Execute( "UPDATE [ServiceJob] SET [IsActive] = 1 WHERE [Id] = @Id", P( "@Id", jobId.Value ) );

            // Rock makes a job's settings the first time its scheduler loads the job. Make Set Visit Dates the way Rock
            // would, with its declared default, and give this job the value False.
            var entityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.ServiceJob'" ) );
            var attributeId = _db.Scalar( @"
SELECT [Id] FROM [Attribute]
WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'Class' AND [EntityTypeQualifierValue] = @Class AND [Key] = 'SetVisitDates'",
                P( "@EntityTypeId", entityTypeId ), P( "@Class", JobClass ) ) as int?;
            if ( !attributeId.HasValue )
            {
                attributeId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = @BooleanFieldType ), @EntityTypeId, 'Class', @Class, 'SetVisitDates', 'Set Visit Dates', 'If enabled will update the first and second visit person attributes.', 3, 0, 'True', 0, 0, NEWID(), @Now, @Now, @ForeignKey )",
                    P( "@BooleanFieldType", BooleanFieldTypeGuid ), P( "@EntityTypeId", entityTypeId ), P( "@Class", JobClass ), P( "@Now", _dates.Anchor ), P( "@ForeignKey", _foreignKey ) ) );
            }

            _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [ValueAsBoolean], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( 0, @AttributeId, @JobId, 'False', 0, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@AttributeId", attributeId.Value ), P( "@JobId", jobId.Value ), P( "@Now", _dates.Anchor ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Runs Rock's giving and attendance count procedures as of the job's last run, the rest of what the job does,
        /// and marks the values they added as this dataset's (decision D-era-4).
        /// </summary>
        private void RunCountProcedures( DateTime now )
        {
            // A fresh bulk load leaves the statistics the empty base had, and Rock's count procedures, which look up each
            // person in turn, then pick plans that take seconds for every hundred people. Fresh statistics on the tables
            // they read make them several times faster.
            using ( var command = _db.CreateCommand( "sp_executesql" ) )
            {
                command.CommandType = System.Data.CommandType.StoredProcedure;
                var tables = new[] { "Attendance", "AttendanceOccurrence", "PersonAlias", "GroupMember", "Group", "Person", "FinancialTransaction", "FinancialTransactionDetail" };
                command.Parameters.Add( new Microsoft.Data.SqlClient.SqlParameter( "@stmt", System.Data.SqlDbType.NVarChar, -1 ) { Value = string.Join( "; ", tables.Select( t => $"UPDATE STATISTICS [{t}]" ) ) } );
                command.ExecuteNonQuery();
            }

            foreach ( var name in new[] { GivingProcedure, AttendanceProcedure } )
            {
                RunPinned( name, now );
            }

            var attributeIds = CountAttributeGuids.Select( g => _db.RequireIdByGuid( "Attribute", g ) ).ToList();
            ProcedureAttributeIds = attributeIds;
            // Only this dataset's people's values go with the dataset. Rock's own people, such as Giver Anonymous, keep theirs, as
            // after any run of Rock's job.
            _db.Execute( $@"
UPDATE [av] SET [av].[ForeignKey] = @ForeignKey
FROM [AttributeValue] AS [av]
INNER JOIN [Person] AS [p] ON [p].[Id] = [av].[EntityId]
WHERE {PeopleLoader.DatasetPersonFilter( "p" )} AND [av].[ForeignKey] IS NULL AND [av].[AttributeId] IN ( {string.Join( ", ", attributeIds )} )", P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Runs one of Rock's procedures as of <paramref name="now"/>. When its clock cannot be pinned, it runs as Rock
        /// installed it, which is the same whenever the anchor is today.
        /// </summary>
        private void RunPinned( string name, DateTime now )
        {
            _db.Recorder?.Procedures.Add( name );
            var procedure = RockProcedureClock.TryRead( _db, name );
            if ( procedure != null )
            {
                using var command = procedure.Command( now );
                command.ExecuteNonQuery();
                return;
            }

            _db.Log.Write( $"  WARNING: {name} reads the clock in a way the loader does not know, so it ran as of today. Check RockProcedureClock against Rock." );
            _db.Execute( $"EXEC [dbo].[{name}]" );
        }

        /// <summary>
        /// Runs Rock's own eRA procedure as of install day inside a savepoint, and reports any family the job would move in
        /// or out, and any value the procedure would change. Then rolls it back.
        /// </summary>
        private void CheckWithRock( DateTime now )
        {
            _db.Recorder?.Procedures.Add( EraProcedure );
            var procedure = RockProcedureClock.TryRead( _db, EraProcedure );
            if ( procedure == null )
            {
                _db.Log.Write( $"  WARNING: {EraProcedure} reads the clock in a way the loader does not know, so the eRA check did not run" );
                return;
            }

            var attributeIds = new[] { CurrentlyAnEraGuid, EraStartDateGuid, EraEndDateGuid }.Select( g => _db.RequireIdByGuid( "Attribute", g ) ).ToList();
            var snapshotSql = $"SELECT [AttributeId], [EntityId], [Value] FROM [AttributeValue] WHERE [AttributeId] IN ( {string.Join( ", ", attributeIds )} )";
            HashSet<string> Snapshot() => _db.Query( snapshotSql ).Select( r => $"{r["AttributeId"]}:{r["EntityId"]}:{r["Value"]}" ).ToHashSet();

            var before = Snapshot();
            _db.Execute( "SAVE TRANSACTION FamilyAnalyticsCheck" );
            var results = new List<EraResult>();
            HashSet<string> after;
            try
            {
                using ( var command = procedure.Command( now ) )
                using ( var reader = command.ExecuteReader() )
                {
                    while ( reader.Read() )
                    {
                        results.Add( new EraResult( reader.GetInt32( 0 ), reader.GetInt32( 1 ), reader.GetInt32( 2 ), reader.GetInt32( 3 ), reader.GetInt32( 4 ), reader.GetInt32( 5 ), reader.GetInt32( 6 ), reader.GetBoolean( 7 ) ) );
                    }
                }

                after = Snapshot();
            }
            finally
            {
                _db.Execute( "ROLLBACK TRANSACTION FamilyAnalyticsCheck" );
            }

            var entering = results.Count( r => !r.IsEra );
            var exiting = results.Count( r => r.IsEra && RockFamilyAnalytics.MeetsExit( r ) );
            var changed = before.Count + after.Count - 2 * before.Intersect( after ).Count();
            _db.Log.Write( entering == 0 && exiting == 0 && changed == 0
                ? $"  eRA check: Rock's own {EraProcedure}, run as of install day, keeps all {results.Count:N0} eRA families and changes nothing"
                : $"  WARNING: Rock's own {EraProcedure}, run as of install day, would enter {entering:N0} families, exit {exiting:N0}, and change {changed:N0} values. Check RockFamilyAnalytics against Rock." );
        }

        private static int DateKey( DateTime date ) => int.Parse( date.ToString( "yyyyMMdd", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture );
    }
}
