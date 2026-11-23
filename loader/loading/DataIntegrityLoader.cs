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
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Rock's Data Integrity pages were mostly empty, because the data is clean on purpose. This step writes the small,
        named set of problems in configuration/data-integrity.json, each the way Rock's own code leaves it: when each Cast
        record was made, the forms that made the Pending ones, the Cast's connection status changes, two people who said
        on Rock's Email Preferences page that they are no longer involved, Cast addresses Rock's address service could not
        match, Person Data Error and Photo Request workflows, and the Data Automation rules, configured and off. It runs
        after every part that writes people, gifts, groups, and notes, and before Rock's attribute value save hook, which
        fills the workflow values' person columns.

        Reason: Every page under Tools > Data Integrity has realistic work waiting (issue 2, decisions D-integrity-1 to
        D-integrity-9).
    */

    /// <summary>
    /// Writes configuration/data-integrity.json: the Cast's record dates and Data Integrity stories, and Rock's Data
    /// Integrity Settings.
    /// </summary>
    internal sealed partial class DataIntegrityLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid DemographicHistoryCategoryGuid = new Guid( "51D3EC5A-D079-45ED-909E-B0AB2FD06835" );
        private static readonly Guid ActiveStatusGuid = new Guid( "618F906C-C33D-4FA3-8AEF-E58CB7B63F1E" );
        private static readonly Guid InactiveStatusGuid = new Guid( "1DAD99D5-41A9-4865-8366-F269902B80A4" );
        private static readonly Guid DeceasedReasonGuid = new Guid( "05D35BC4-5816-4210-965F-1BF44F35A16A" );
        private static readonly Guid SelfInactivatedReviewReasonGuid = new Guid( "D539C356-6856-4E94-80B4-8FEA869AF38B" );
        private static readonly Guid RecordSourceTypeGuid = new Guid( "B8F781C5-6690-4E5E-8BCE-75CC62DB0EBB" );

        /// <summary>
        /// The verb and change type of a Rock History line for a changed person property.
        /// </summary>
        internal const string ModifyVerb = "MODIFY";

        internal const string PropertyChangeType = "Property";

        internal const string ConnectionStatusValueName = "Connection Status";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly DataTable _history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "Verb:string", "ChangeType:string",
            "ValueName:string", "OldValue:string", "NewValue:string", "OldRawValue:string", "NewRawValue:string", "IsSensitive:bool", "Guid:guid", "CreatedDateTime:date", "ModifiedDateTime:date",
            "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );

        private int _personTypeId;
        private int _historyCategoryId;

        public DataIntegrityLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Writes everything in configuration/data-integrity.json for the Cast people this dataset loads. Returns the log lines.
        /// </summary>
        public List<string> Load( PeopleLoader people, IReadOnlyCollection<string> castHouseholdKeys, bool arePhotosLoaded )
        {
            var config = _source.DataIntegrity;
            if ( config == null )
            {
                return new List<string> { "  Skipped (this package has no configuration/data-integrity.json)" };
            }

            _personTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            _historyCategoryId = _db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid );
            var cast = people.Households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ).ToList();
            var log = new List<string>
            {
                WriteSettings( config.Settings ),
                WriteRecordSources( config.Pending, people ),
                WriteStatusHistory( config.ConnectionStatusHistory, people ),
                WriteSelfInactivations( config.SelfInactivations, people, cast ),
                WriteFailedAddresses( config.FailedAddresses, people, cast ),
                WriteDataErrors( config.DataErrors, people ),
                WritePhotoRequests( config.PhotoRequests, people, arePhotosLoaded )
            };

            // Record dates come last, since no record is made after the first thing written for its person, these stories included.
            _db.BulkInsert( "History", _history, keepIdentity: false );
            log.Insert( 1, WriteRecordDates( config, cast ) );
            return log;
        }

        /// <summary>
        /// A relative date with the given time of day when the date names none.
        /// </summary>
        private DateTime At( RelativeDate date, string time )
        {
            var resolved = _dates.Resolve( date ).Value;
            return string.IsNullOrEmpty( date.Time ) ? resolved.Date.Add( TimeSpan.Parse( time, CultureInfo.InvariantCulture ) ) : resolved;
        }

        /// <summary>
        /// A relative date that must name its time.
        /// </summary>
        private DateTime At( RelativeDate date ) => _dates.Resolve( date ).Value;

        private static string FullName( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";

        /// <summary>
        /// Adds a line to the person's History, as Rock's person save hook writes one for a changed property. Rock fills the
        /// raw values only when both the old and the new value are set.
        /// </summary>
        private void AddHistory( LoadedPerson person, string valueName, string oldValue, string newValue, int? oldRaw, int? newRaw, DateTime when, int byAliasId, string key )
        {
            var hasBoth = !string.IsNullOrEmpty( oldValue ) && !string.IsNullOrEmpty( newValue );
            _history.Rows.Add( false, _historyCategoryId, _personTypeId, person.PersonId, FullName( person ), ModifyVerb, PropertyChangeType, valueName,
                Text( oldValue ), Text( newValue ), hasBoth && oldRaw.HasValue ? oldRaw.Value.ToString( CultureInfo.InvariantCulture ) : DBNull.Value,
                hasBoth && newRaw.HasValue ? newRaw.Value.ToString( CultureInfo.InvariantCulture ) : DBNull.Value, false,
                SeededRandom.StableGuid( $"data-integrity:history:{person.Cast.Guid}:{key}" ), when, when, byAliasId, byAliasId, _foreignKey );
        }

        private static object Text( string text ) => string.IsNullOrEmpty( text ) ? DBNull.Value : text;

        /*
            11/23/26 - CLAUDE

            Every person row carried the load's own time as its created date, so Rock's Pending Individuals report showed
            each Pending record made on install day, and Rock's Connection Status Changes report listed every person as new.
            A Cast record is now made on its household's first visit, or for a child born later, three weeks after birth. A
            Pending record carries the date of the form that made it. A household with no first visit takes the date in the
            file, or else its first record. And no record is made after its person's first record: a check-in, gift, group
            membership, request, registration, note, login, workflow, or History line Rock writes as it happens. Rock's
            admin person keeps the time Rock made it (decision 177). Lines Rock dates by an earlier event, such as a
            baptism, and communication lists and prayer requests, which other parts date without a first visit, are not
            first records.

            Reason: The Pending report and the status history read true (decision D-integrity-1).
        */

        /// <summary>
        /// Sets each Cast person's created date, and each Cast family's to its first member's.
        /// </summary>
        private string WriteRecordDates( DataIntegrityConfiguration config, List<LoadedHousehold> cast )
        {
            var rule = config.RecordDates;
            var pendingDates = config.Pending.Records.Where( r => r.Created != null ).ToDictionary( r => r.Person, r => At( r.Created ) );
            var latest = _dates.Anchor.Date;
            var dated = new Dictionary<int, DateTime>();
            var undated = new List<string>();
            var firstRecords = FirstRecords( _db, cast.SelectMany( h => h.Members ).Select( m => m.PersonId ) );
            foreach ( var household in cast )
            {
                var own = household.Members.Where( m => household.Cast.Members.Any( c => c.Guid == m.Cast.Guid ) && !m.IsRockAdmin ).ToList();
                var householdFirst = own.Where( m => firstRecords.ContainsKey( m.PersonId ) ).Select( m => ( DateTime? ) firstRecords[m.PersonId] ).Min();
                DateTime? first = rule.Households.TryGetValue( household.Cast.Key, out var given ) ? At( given, rule.Time )
                    : household.Cast.Attendance?.Started != null ? At( household.Cast.Attendance.Started, rule.Time )
                    : householdFirst;
                foreach ( var person in own )
                {
                    DateTime? created = pendingDates.TryGetValue( person.Cast.Guid, out var formDate ) ? formDate : first;
                    if ( created.HasValue && person.BirthDate.HasValue && person.BirthDate.Value > created.Value.Date )
                    {
                        var added = person.BirthDate.Value.AddDays( rule.NewbornDays ).Add( created.Value.TimeOfDay );
                        created = added < latest ? added : latest;
                    }

                    if ( firstRecords.TryGetValue( person.PersonId, out var firstRecord ) && ( !created.HasValue || firstRecord < created.Value ) )
                    {
                        created = firstRecord;
                    }

                    if ( created.HasValue )
                    {
                        dated[person.PersonId] = created.Value;
                    }
                    else
                    {
                        undated.Add( FullName( person ) );
                    }
                }
            }

            foreach ( var batch in dated.Select( ( d, i ) => (d, i) ).GroupBy( x => x.i / 500, x => x.d ) )
            {
                var values = string.Join( ", ", batch.Select( d => $"( {d.Key}, '{d.Value:yyyy-MM-ddTHH:mm:ss}' )" ) );
                _db.Execute( $"UPDATE [p] SET [p].[CreatedDateTime] = CAST( [v].[Created] AS DATETIME ) FROM [Person] AS [p] INNER JOIN ( VALUES {values} ) AS [v] ( [Id], [Created] ) ON [v].[Id] = [p].[Id]" );
            }

            var familyIds = string.Join( ", ", cast.Select( h => h.FamilyId ) );
            if ( familyIds.Length > 0 )
            {
                _db.Execute( $@"
UPDATE [g] SET [g].[CreatedDateTime] = [first].[Created]
FROM [Group] AS [g]
CROSS APPLY ( SELECT MIN( [p].[CreatedDateTime] ) AS [Created] FROM [GroupMember] AS [gm] INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId] WHERE [gm].[GroupId] = [g].[Id] ) AS [first]
WHERE [g].[Id] IN ( {familyIds} ) AND [first].[Created] < [g].[CreatedDateTime]" );
            }

            return undated.Count == 0
                ? $"  Cast record dates: {dated.Count:N0} people"
                : $"  Cast record dates: {dated.Count:N0} people. WARNING: no date for {undated.Count:N0}, who keep the load's time: {string.Join( ", ", undated )}";
        }

        /// <summary>
        /// The kinds of rows Rock writes for a person as things happen, so the person's record must already exist. Each
        /// gives a person Id and a time. Communication lists, prayer requests, emails, and History lines Rock dates by an
        /// earlier event are left out (see WriteRecordDates). {0} is the list of person Ids.
        /// </summary>
        internal const string FirstRecordsSql = @"
SELECT [r].[PersonId], MIN( [r].[At] ) AS [At]
FROM (
    SELECT [pa].[PersonId], [a].[StartDateTime] AS [At] FROM [Attendance] AS [a] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
    UNION ALL SELECT [gm].[PersonId], [gm].[DateTimeAdded] FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
        WHERE [g].[IsSecurityRole] = 0 AND [gt].[Guid] NOT IN ( '790E3215-3B10-442B-AF69-616C0DCB998E', 'E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF', 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' )
    UNION ALL SELECT [pa].[PersonId], [t].[TransactionDateTime] FROM [FinancialTransaction] AS [t] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
    UNION ALL SELECT [pa].[PersonId], [r].[CreatedDateTime] FROM [ConnectionRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
    UNION ALL SELECT [pa].[PersonId], [r].[CreatedDateTime] FROM [RegistrationRegistrant] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
    UNION ALL SELECT [pa].[PersonId], [r].[RequestDateTime] FROM [BenevolenceRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[RequestedByPersonAliasId]
    UNION ALL SELECT [n].[EntityId], [n].[CreatedDateTime] FROM [Note] AS [n] INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId] WHERE [nt].[EntityTypeId] = @PersonType
    UNION ALL SELECT [u].[PersonId], [u].[CreatedDateTime] FROM [UserLogin] AS [u] WHERE [u].[PersonId] IS NOT NULL
    UNION ALL SELECT [w].[EntityId], [w].[ActivatedDateTime] FROM [Workflow] AS [w] WHERE [w].[EntityTypeId] = @PersonType
    UNION ALL SELECT [h].[EntityId], [h].[CreatedDateTime] FROM [History] AS [h] WHERE [h].[EntityTypeId] = @PersonType AND [h].[CategoryId] = @Demographics AND [h].[ValueName] <> 'Person'
) AS [r]
WHERE [r].[At] IS NOT NULL AND [r].[PersonId] IN ( {0} )
GROUP BY [r].[PersonId]";

        /// <summary>
        /// Each person's first record (see <see cref="FirstRecordsSql"/>), by person Id. A person with none is left out.
        /// </summary>
        internal static Dictionary<int, DateTime> FirstRecords( RockDatabase db, IEnumerable<int> personIds )
        {
            var ids = personIds.Distinct().ToList();
            if ( ids.Count == 0 )
            {
                return new Dictionary<int, DateTime>();
            }

            // The Ids go inline: the contract recorder reads every INSERT as a write to a Rock table.
            var sql = string.Format( FirstRecordsSql, string.Join( ", ", ids ) );
            return db.Query( sql, P( "@PersonType", db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid ) ), P( "@Demographics", db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid ) ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["At"] );
        }

        /*
            11/23/26 - CLAUDE

            Rock's web forms make a new person Pending and set the form's Record Source, such as Group Registration or
            External Website, so staff review the record before it counts. The Cast's Pending records each name their form.

            Reason: Rock's Pending Individuals report lists records nobody reviewed, each with where it came from
            (decision D-integrity-8).
        */

        /// <summary>
        /// Gives each Pending Cast record the Record Source of the form that made it.
        /// </summary>
        private string WriteRecordSources( PendingRecords pending, PeopleLoader people )
        {
            var sourceIds = _db.Query( "SELECT [v].[Id], [v].[Value] FROM [DefinedValue] AS [v] INNER JOIN [DefinedType] AS [t] ON [t].[Id] = [v].[DefinedTypeId] WHERE [t].[Guid] = @Type",
                    P( "@Type", RecordSourceTypeGuid ) )
                .ToDictionary( r => ( string ) r["Value"], r => ( int ) r["Id"], StringComparer.OrdinalIgnoreCase );
            var written = 0;
            foreach ( var record in pending.Records.Where( r => r.Source != null && people.People.ContainsKey( r.Person ) ) )
            {
                if ( !sourceIds.TryGetValue( record.Source, out var sourceId ) )
                {
                    throw new InvalidDataException( $"configuration/data-integrity.json gives {record.Name} the Record Source \"{record.Source}\", which this Rock does not have." );
                }

                written += _db.Execute( "UPDATE [Person] SET [RecordSourceValueId] = @Source WHERE [Id] = @Id", P( "@Source", sourceId ), P( "@Id", people.People[record.Person].PersonId ) );
            }

            return $"  Pending records with the form's Record Source: {written:N0}";
        }

        /*
            11/23/26 - CLAUDE

            Rock's person save hook writes a History line when a person's connection status changes, and Rock's Connection
            Status Changes report reads those lines (ConnectionStatusChangeReportBuilder: the demographic changes category,
            change type Property, value name Connection Status). The line holds the old and new status names and, when both
            are set, their Ids, and it is stamped by the staff member who saved it.

            Reason: The report and each person's History show how Cast people moved along (decision D-integrity-2).
        */

        /// <summary>
        /// Writes each Cast connection status change as the History line Rock's person save hook writes.
        /// </summary>
        private string WriteStatusHistory( ConnectionStatusHistory history, PeopleLoader people )
        {
            var statusIds = _source.ConnectionStatuses.Statuses.ToDictionary( s => s.Name, s => _db.RequireIdByGuid( "DefinedValue", s.Guid ), StringComparer.OrdinalIgnoreCase );
            var count = 0;
            foreach ( var entry in history.People )
            {
                foreach ( var named in entry.People.Where( p => people.People.ContainsKey( p.Person ) ) )
                {
                    var person = people.People[named.Person];
                    foreach ( var change in entry.Changes )
                    {
                        var by = people.People[change.By].AliasId;
                        AddHistory( person, ConnectionStatusValueName, change.From, change.To, statusIds[change.From], statusIds[change.To], At( change.Changed ), by, $"status:{change.To}" );
                        count++;
                    }
                }
            }

            return $"  Connection status changes: {count:N0}";
        }

        /*
            11/23/26 - CLAUDE

            Rock's Email Preferences page (EmailPreferenceEntry, DeactivateRecord) lets a person say they are no longer
            involved. Rock's InactivatePerson sets the reason, the note, and the Self Inactivated review reason, and its
            save hook stamps when the status changed. When the person is an adult in exactly one family, the page's Allow
            Inactivating Family setting, on by default, does the same for every member. Rock's Self Inactivated report is
            Inactive people with that review reason. The household files already say inactive with the reason, so this adds
            what the page wrote and the History lines Rock's save hook writes for it.

            Reason: Rock's Self Inactivated Individuals report shows real people with their own words (decision D-integrity-3).
        */

        /// <summary>
        /// Writes each self-inactivation: the notes and review reason on the person, or on their whole family, and the History lines.
        /// </summary>
        private string WriteSelfInactivations( SelfInactivations inactivations, PeopleLoader people, List<LoadedHousehold> cast )
        {
            var activeId = _db.RequireIdByGuid( "DefinedValue", ActiveStatusGuid );
            var inactiveId = _db.RequireIdByGuid( "DefinedValue", InactiveStatusGuid );
            var reviewId = _db.RequireIdByGuid( "DefinedValue", SelfInactivatedReviewReasonGuid );
            string ValueOf( int id ) => ( string ) _db.Scalar( "SELECT [Value] FROM [DefinedValue] WHERE [Id] = @Id", P( "@Id", id ) );
            var names = new List<string>();
            foreach ( var record in inactivations.Records.Where( r => people.People.ContainsKey( r.Person ) ) )
            {
                var person = people.People[record.Person];
                var families = cast.Where( h => h.Members.Any( m => m.PersonId == person.PersonId ) ).ToList();
                var targets = person.IsAdult && families.Count == 1 ? families[0].Members : new List<LoadedPerson> { person };
                var reasonId = Convert.ToInt32( _db.Scalar( "SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Value] = @Name AND [DefinedTypeId] = ( SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Guid] = @Deceased )",
                    P( "@Name", record.Reason ), P( "@Deceased", DeceasedReasonGuid ) ) ?? throw new InvalidDataException( $"{record.Name}'s reason \"{record.Reason}\" is not one of Rock's Record Status Reasons." ) );
                var when = At( record.Inactivated );
                foreach ( var target in targets )
                {
                    if ( target.Cast.RecordStatus != "inactive" || !string.Equals( target.Cast.RecordStatusReason, record.Reason, StringComparison.OrdinalIgnoreCase ) )
                    {
                        throw new InvalidDataException( $"{FullName( target )} is inactivated by {record.Name}'s self-inactivation, so their household file must say inactive with the reason {record.Reason}." );
                    }

                    _db.Execute( @"
UPDATE [Person]
SET [RecordStatusReasonValueId] = @Reason, [InactiveReasonNote] = @Note, [ReviewReasonValueId] = @Review, [ReviewReasonNote] = @Note,
    [RecordStatusLastModifiedDateTime] = @When, [ModifiedDateTime] = @When, [ModifiedByPersonAliasId] = @By
WHERE [Id] = @Id",
                        P( "@Reason", reasonId ), P( "@Note", record.Note ), P( "@Review", reviewId ), P( "@When", when ), P( "@By", person.AliasId ), P( "@Id", target.PersonId ) );

                    AddHistory( target, "Record Status", ValueOf( activeId ), ValueOf( inactiveId ), activeId, inactiveId, when, person.AliasId, "self-inactivation:status" );
                    AddHistory( target, "Record Status Reason", null, ValueOf( reasonId ), null, reasonId, when, person.AliasId, "self-inactivation:reason" );
                    AddHistory( target, "Inactive Reason Note", null, record.Note, null, null, when, person.AliasId, "self-inactivation:note" );
                    AddHistory( target, "Review Reason", null, ValueOf( reviewId ), null, reviewId, when, person.AliasId, "self-inactivation:review" );
                    AddHistory( target, "Review Reason Note", null, record.Note, null, null, when, person.AliasId, "self-inactivation:review-note" );
                    names.Add( FullName( target ) );
                }
            }

            return $"  Self-inactivated: {names.Count:N0} ({string.Join( ", ", names )})";
        }

        /*
            11/23/26 - CLAUDE

            Rock's address service could not match a few new Cast homes, so LocationService.Verify left both attempt dates,
            the service's name, and its result, and no success dates or map point. On its own, Rock's Location Services
            Verify job would send such an address again once its retry period ends, and sample addresses never go to an
            address service (decision 316). Rock's own way to stop that is Location Locked on the Location Detail page,
            which the job and Verify both skip. So a staff member locked each one, and it waits on the Location Editor
            under Not Geocoded for someone to place its pin.

            Reason: Rock's Location Editor has real work, and no sample address ever leaves the server (decision D-integrity-4).
        */

        /// <summary>
        /// Marks each failed Cast home as Rock's address service left it, and locked.
        /// </summary>
        private string WriteFailedAddresses( FailedAddresses failed, PeopleLoader people, List<LoadedHousehold> cast )
        {
            var names = new List<string>();
            foreach ( var entry in failed.Households )
            {
                var household = cast.FirstOrDefault( h => h.Cast.Key == entry.Household );
                if ( household == null )
                {
                    continue;
                }

                var locationId = household.HomeLocationId ?? throw new InvalidDataException( $"configuration/data-integrity.json lists {entry.Household} as a failed address, but the household has no home address." );
                if ( cast.Concat( people.Households ).Any( h => h.HomeLocationId == locationId && h.Cast.Key != entry.Household ) )
                {
                    throw new InvalidDataException( $"{entry.Household}'s home is another household's home too, so it cannot fail for one of them." );
                }

                _db.Execute( @"
UPDATE [Location]
SET [StandardizedDateTime] = NULL, [GeocodedDateTime] = NULL, [StandardizeAttemptedResult] = @Result, [GeocodeAttemptedResult] = @Result,
    [GeoPoint] = NULL, [IsGeoPointLocked] = 1, [ModifiedByPersonAliasId] = @By
WHERE [Id] = @Id",
                    P( "@Result", failed.Result ), P( "@By", people.People[failed.LockedBy].AliasId ), P( "@Id", locationId ) );
                names.Add( household.Cast.Name );
            }

            return $"  Addresses Rock's address service could not match, locked: {names.Count:N0} ({string.Join( ", ", names )})";
        }
    }
}
