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
    internal sealed partial class DataIntegrityLoader
    {
        /// <summary>
        /// A household's first record: when it was, and the Record Source of the form that writes that kind of record, or
        /// null for a kind no form writes, such as a note.
        /// </summary>
        private readonly record struct FirstRecord( DateTime At, string Source );

        /// <summary>
        /// A generated household whose record date is open: no first visit fixes it.
        /// </summary>
        private sealed record OpenHousehold( LoadedHousehold Household, List<LoadedPerson> People, FirstRecord? Record );

        /// <summary>
        /// A Cast household with no first visit whose first record comes this soon after the history window opens was
        /// already coming, so its record was made before the window.
        /// </summary>
        private const int AlreadyComingDays = 56;

        /*
            11/23/26 - CLAUDE

            Every person row carried the load's own time as its created date, so Rock's Pending Individuals report showed
            each Pending record made on install day, and Rock's Connection Status Changes report listed every person as new.

            A record is made on its household's first visit, or for a child born later, three weeks after birth. A Pending
            Cast record carries the date of the form that made it. Any other Cast household takes the date in the file, or
            else its first record. A first record in the window's first eight weeks shows the household was already coming,
            so its record was made in the years before.

            The rest of the generated households have no first visit to date them. Each month of the history window gets
            source-1's rate of new records, in source-1's months, counting the first visits already dated. A month fills
            first with households whose first record falls in it, made by that record, such as a first gift. Then come
            households with no record at all, and then households whose first record comes later. Every household left
            over was made in the years before the window, more often recently. So a household that gives twice a year is
            not new because its first gift in the window came in March.

            No record is made after its person's first record: a check-in, gift, group membership, request, registration,
            note, login, workflow, or History line Rock writes as it happens. Rock's admin person keeps the time Rock made
            it (decision 177). Lines Rock dates by an earlier event, such as a baptism, and communication lists and prayer
            requests, which other parts date without a first visit, are not first records. Each family takes its first
            member's date.

            Each record also gets a Record Source: the form a first visit, a first record, or a Pending record came from,
            or a mix from configuration/data-integrity.json for records made earlier.

            Reason: The Pending report and the status history read true, records come at a real church's rate, and each
            record shows where it came from (decisions D-integrity-1 and D-integrity-10).
        */

        /// <summary>
        /// Sets every person's created date and Record Source, and each family's created date to its first member's.
        /// </summary>
        private List<string> WriteRecordDates( DataIntegrityConfiguration config, IReadOnlyCollection<LoadedHousehold> households, IReadOnlyCollection<string> castHouseholdKeys )
        {
            var rule = config.RecordDates;
            var sourceIds = RecordSourceIds( rule.Sources );
            var pendingDates = config.Pending.Records.Where( r => r.Created != null ).ToDictionary( r => r.Person, r => At( r.Created ) );
            var firstRecords = FirstRecordsForDataset( _db, _foreignKey );
            var dated = new Dictionary<int, (DateTime At, string Source)>();
            var undated = new List<string>();
            var open = new List<OpenHousehold>();

            foreach ( var household in households )
            {
                var own = household.Members.Where( m => household.Cast.Members.Any( c => c.Guid == m.Cast.Guid ) && !m.IsRockAdmin ).ToList();
                if ( own.Count == 0 )
                {
                    continue;
                }

                var record = own.Where( m => firstRecords.ContainsKey( m.PersonId ) ).Select( m => ( FirstRecord? ) firstRecords[m.PersonId] ).OrderBy( r => r.Value.At ).FirstOrDefault();
                DateTime? first;
                string source;
                if ( rule.Households.TryGetValue( household.Cast.Key, out var given ) )
                {
                    first = At( given, rule.Time );
                    source = Draw( rule.Sources.Earlier, household.Cast.Guid );
                }
                else if ( household.Cast.Attendance?.Started != null )
                {
                    first = At( household.Cast.Attendance.Started, rule.Time );
                    source = FirstVisitSource( rule.Sources, own, first.Value );
                }
                else if ( castHouseholdKeys.Contains( household.Cast.Key ) )
                {
                    var alreadyComing = record.HasValue && record.Value.At < _historyStart.AddDays( AlreadyComingDays );
                    first = alreadyComing ? BeforeHistory( rule.Generated, MonthWeights(), household.Cast.Guid ) : record?.At;
                    source = alreadyComing ? Draw( rule.Sources.Earlier, household.Cast.Guid ) : record?.Source ?? Draw( rule.Sources.Earlier, household.Cast.Guid );
                }
                else
                {
                    open.Add( new OpenHousehold( household, own, record ) );
                    continue;
                }

                foreach ( var person in own )
                {
                    var created = PersonCreated( rule, person, pendingDates.TryGetValue( person.Cast.Guid, out var formDate ) ? formDate : first, source, firstRecords );
                    if ( created.At.HasValue )
                    {
                        dated[person.PersonId] = (created.At.Value, created.Source);
                    }
                    else
                    {
                        undated.Add( FullName( person ) );
                    }
                }
            }

            var (filled, perMonth, target) = FillHistoryWindow( rule, open, firstRecords, dated );
            WriteCreatedDates( dated, sourceIds );

            var bySource = _db.Query( @"
SELECT ISNULL( [v].[Value], 'none' ) AS [Source], COUNT(*) AS [People]
FROM [Person] AS [p] LEFT JOIN [DefinedValue] AS [v] ON [v].[Id] = [p].[RecordSourceValueId]
WHERE [p].[ForeignKey] = @ForeignKey GROUP BY [v].[Value] ORDER BY COUNT(*) DESC", P( "@ForeignKey", _foreignKey ) );
            return new List<string>
            {
                undated.Count == 0
                    ? $"  Record dates: {dated.Count:N0} people. {filled:N0} were made inside the history window with no first visit. Records made in the last {MeasuredMonths} full months: {perMonth:N0} a month, against source-1's {target:N0}"
                    : $"  Record dates: {dated.Count:N0} people. WARNING: no date for {undated.Count:N0}, who keep the load's time: {string.Join( ", ", undated.Take( 20 ) )}",
                "  Record sources: " + string.Join( ", ", bySource.Select( r => $"{r["Source"]} {Convert.ToInt32( r["People"] ):N0}" ) )
            };
        }

        /// <summary>
        /// A household that first came with a child under 18 came through check-in. A household of adults filled out the
        /// website's connection card.
        /// </summary>
        private static string FirstVisitSource( RecordSourceRule rule, List<LoadedPerson> people, DateTime visit )
        {
            return people.Any( p => p.BirthDate.HasValue && p.BirthDate.Value <= visit && p.BirthDate.Value.AddYears( 18 ) > visit ) ? rule.FirstVisitWithChildren : rule.FirstVisitAdults;
        }

        /// <summary>
        /// A person's created date and Record Source from their household's: three weeks after birth, from check-in, for a
        /// child born later, and never after their own first record, which then names the source.
        /// </summary>
        private (DateTime? At, string Source) PersonCreated( RecordDatesRule rule, LoadedPerson person, DateTime? householdCreated, string householdSource, Dictionary<int, FirstRecord> firstRecords )
        {
            var created = householdCreated;
            var source = householdSource;
            if ( created.HasValue && person.BirthDate.HasValue && person.BirthDate.Value > created.Value.Date )
            {
                var added = person.BirthDate.Value.AddDays( rule.NewbornDays ).Add( created.Value.TimeOfDay );
                created = added < _dates.Anchor.Date ? added : _dates.Anchor.Date;
                source = rule.Sources.Newborn;
            }

            return firstRecords.TryGetValue( person.PersonId, out var record ) && ( !created.HasValue || record.At < created.Value )
                ? (record.At, record.Source ?? source)
                : (created, source);
        }

        /// <summary>
        /// Dates the open households. Each month of the history window, latest first, takes households until its records
        /// reach source-1's rate: first those whose first record falls in it, then those with no record, then those whose
        /// first record comes later. The rest were made before the window. Returns how many people were made inside the
        /// window this way, and the records a month in the last full months with the rate they aim for.
        /// </summary>
        private (int Filled, double PerMonth, double Target) FillHistoryWindow( RecordDatesRule rule, List<OpenHousehold> open, Dictionary<int, FirstRecord> firstRecords,
            Dictionary<int, (DateTime At, string Source)> dated )
        {
            var monthWeights = MonthWeights();
            var activePeople = Convert.ToInt32( _db.Scalar( @"
SELECT COUNT(*) FROM [Person] AS [p]
WHERE [p].[RecordTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '36CF10D6-C695-413D-8E7C-4546EFEF385E' )
    AND [p].[RecordStatusValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = '618F906C-C33D-4FA3-8AEF-E58CB7B63F1E' ) AND [p].[IsDeceased] = 0" ) );
            var perMonth = _profile.Number( "people.newPeoplePerMonthPctOfActive", 0 ) / 100 * activePeople;

            var counts = new Dictionary<DateTime, int>();
            void Count( DateTime at )
            {
                var month = new DateTime( at.Year, at.Month, 1 );
                counts[month] = counts.GetValueOrDefault( month ) + 1;
            }

            foreach ( var (at, _) in dated.Values )
            {
                Count( at );
            }

            var left = open.OrderBy( h => SeededRandom.StableGuid( $"data-integrity:record-fill:{h.Household.Cast.Guid}" ) ).ToList();
            var filled = 0;
            void Date( OpenHousehold household, DateTime created, string source )
            {
                foreach ( var person in household.People )
                {
                    var personCreated = PersonCreated( rule, person, created, source, firstRecords );
                    dated[person.PersonId] = (personCreated.At.Value, personCreated.Source);
                    Count( personCreated.At.Value );
                }
            }

            for ( var month = new DateTime( _dates.Anchor.Year, _dates.Anchor.Month, 1 ); month.AddMonths( 1 ) > _historyStart; month = month.AddMonths( -1 ) )
            {
                var from = month > _historyStart ? month : _historyStart;
                var to = month.AddMonths( 1 ) < _dates.Anchor ? month.AddMonths( 1 ) : _dates.Anchor;
                var target = perMonth * monthWeights[month.Month - 1].Weight * ( to - from ).TotalDays / DateTime.DaysInMonth( month.Year, month.Month );
                var candidates = left.Where( h => h.Record.HasValue && h.Record.Value.At >= from && h.Record.Value.At < to )
                    .Concat( left.Where( h => !h.Record.HasValue ) )
                    .Concat( left.Where( h => h.Record.HasValue && h.Record.Value.At >= to ) )
                    .ToList();
                var taken = new HashSet<OpenHousehold>();
                foreach ( var household in candidates )
                {
                    if ( counts.GetValueOrDefault( month ) >= target )
                    {
                        break;
                    }

                    var random = new SeededRandom( $"data-integrity:record-date:{household.Household.Cast.Guid}" );
                    if ( household.Record.HasValue && household.Record.Value.At < to )
                    {
                        Date( household, household.Record.Value.At, household.Record.Value.Source ?? Draw( rule.Sources.Earlier, household.Household.Cast.Guid ) );
                    }
                    else
                    {
                        var day = from.Date.AddDays( random.Between( 0, Math.Max( 0, ( int ) ( to - from.Date ).TotalDays - 1 ) ) );
                        var created = day.AddMinutes( random.Between( 9 * 60, 21 * 60 ) );
                        created = created < from ? from : created >= to ? to.AddMinutes( -1 ) : created;
                        Date( household, created, Draw( household.Record.HasValue ? rule.Sources.Earlier : rule.Sources.NoRecord, household.Household.Cast.Guid ) );
                    }

                    filled += household.People.Count;
                    taken.Add( household );
                }

                left.RemoveAll( taken.Contains );
            }

            foreach ( var household in left )
            {
                Date( household, BeforeHistory( rule.Generated, monthWeights, household.Household.Cast.Guid ), Draw( rule.Sources.Earlier, household.Household.Cast.Guid ) );
            }

            // Source-1's rate counts the full months before install day's month.
            var measuredTo = new DateTime( _dates.Anchor.Year, _dates.Anchor.Month, 1 );
            var measured = counts.Where( c => c.Key >= measuredTo.AddMonths( -MeasuredMonths ) && c.Key < measuredTo ).Sum( c => c.Value );
            return (filled, ( double ) measured / MeasuredMonths, perMonth);
        }

        /// <summary>
        /// Records made in this many full months before install day's month are the ones source-1's new records per month
        /// counts.
        /// </summary>
        private const int MeasuredMonths = 24;

        /// <summary>
        /// A date before the history window: some whole years before it, fewer the further back, in a month weighted by when
        /// source-1's new records come, on a seeded day.
        /// </summary>
        private DateTime BeforeHistory( GeneratedRecordDates rule, IReadOnlyList<(int Month, double Weight)> monthWeights, Guid householdGuid )
        {
            var random = new SeededRandom( $"data-integrity:record-date:{householdGuid}" );
            var years = 0;
            while ( years < rule.YearsBeforeHistory - 1 && random.Chance( rule.YearWeight ) )
            {
                years++;
            }

            var month = PickMonth( monthWeights, random );
            var year = _historyStart.Year - years - ( month >= _historyStart.Month ? 1 : 0 );
            var day = random.Between( 1, DateTime.DaysInMonth( year, month ) );
            return new DateTime( year, month, day ).AddMinutes( random.Between( 9 * 60, 17 * 60 ) );
        }

        /// <summary>
        /// Source-1's month index of new records, as months 1 to 12 with their weights. Even weights when the profile has none.
        /// </summary>
        private IReadOnlyList<(int Month, double Weight)> MonthWeights()
        {
            var index = _profile.Map( "people.newPeopleMonthIndex" );
            return Enumerable.Range( 1, 12 ).Select( m => (m, index.TryGetValue( m.ToString( "00" ), out var w ) ? w : 1.0) ).ToList();
        }

        private static int PickMonth( IReadOnlyList<(int Month, double Weight)> weights, SeededRandom random )
        {
            var roll = random.Between( 0.0, weights.Sum( w => w.Weight ) );
            foreach ( var (month, weight) in weights )
            {
                roll -= weight;
                if ( roll <= 0 )
                {
                    return month;
                }
            }

            return weights[weights.Count - 1].Month;
        }

        /// <summary>
        /// A household's Record Source drawn from a weighted mix, the same on every load.
        /// </summary>
        private static string Draw( Dictionary<string, double> mix, Guid householdGuid )
        {
            var random = new SeededRandom( $"data-integrity:record-source:{householdGuid}" );
            var ordered = mix.OrderBy( m => m.Key, StringComparer.Ordinal ).ToList();
            var roll = random.Between( 0.0, ordered.Sum( m => m.Value ) );
            foreach ( var (source, weight) in ordered )
            {
                roll -= weight;
                if ( roll <= 0 )
                {
                    return source;
                }
            }

            return ordered[ordered.Count - 1].Key;
        }

        /// <summary>
        /// The Id of each Record Source by name. Every name the rule uses must be one of Rock's.
        /// </summary>
        private Dictionary<string, int> RecordSourceIds( RecordSourceRule rule )
        {
            var ids = _db.Query( "SELECT [v].[Id], [v].[Value] FROM [DefinedValue] AS [v] INNER JOIN [DefinedType] AS [t] ON [t].[Id] = [v].[DefinedTypeId] WHERE [t].[Guid] = @Type",
                    P( "@Type", RecordSourceTypeGuid ) )
                .ToDictionary( r => ( string ) r["Value"], r => ( int ) r["Id"], StringComparer.OrdinalIgnoreCase );
            if ( rule.Earlier.Count == 0 || rule.NoRecord.Count == 0 )
            {
                throw new InvalidDataException( "configuration/data-integrity.json needs both Record Source mixes, earlier and noRecord." );
            }

            var named = new[] { rule.FirstVisitWithChildren, rule.FirstVisitAdults, rule.Newborn }.Concat( rule.Earlier.Keys ).Concat( rule.NoRecord.Keys ).Concat( FirstRecordSources );
            foreach ( var name in named.Where( n => !ids.ContainsKey( n ) ) )
            {
                throw new InvalidDataException( $"configuration/data-integrity.json names the Record Source \"{name}\", which this Rock does not have." );
            }

            return ids;
        }

        /// <summary>
        /// Writes each person's created date and Record Source, keeping a Pending record's form, then each family's created
        /// date from its first member.
        /// </summary>
        private void WriteCreatedDates( Dictionary<int, (DateTime At, string Source)> dated, Dictionary<string, int> sourceIds )
        {
            var rows = TableFactory.Create( "PersonId:int", "Created:date", "SourceId:int" );
            foreach ( var (personId, (created, source)) in dated )
            {
                rows.Rows.Add( personId, created, sourceIds[source] );
            }

            _db.Execute( "CREATE TABLE #RecordDates ( [PersonId] INT NOT NULL PRIMARY KEY, [Created] DATETIME NOT NULL, [SourceId] INT NOT NULL )" );
            _db.BulkInsert( "#RecordDates", rows, keepIdentity: false );
            _db.Execute( @"
UPDATE [p] SET [p].[CreatedDateTime] = [d].[Created], [p].[RecordSourceValueId] = ISNULL( [p].[RecordSourceValueId], [d].[SourceId] )
FROM [Person] AS [p] INNER JOIN #RecordDates AS [d] ON [d].[PersonId] = [p].[Id]" );
            _db.Execute( @"
UPDATE [g] SET [g].[CreatedDateTime] = [first].[Created]
FROM [Group] AS [g]
INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId] AND [gt].[Guid] = '790E3215-3B10-442B-AF69-616C0DCB998E'
CROSS APPLY ( SELECT MIN( [d].[Created] ) AS [Created] FROM [GroupMember] AS [gm] INNER JOIN #RecordDates AS [d] ON [d].[PersonId] = [gm].[PersonId] WHERE [gm].[GroupId] = [g].[Id] ) AS [first]
WHERE [g].[ForeignKey] = @ForeignKey AND [first].[Created] < [g].[CreatedDateTime]", P( "@ForeignKey", _foreignKey ) );
            _db.Execute( "DROP TABLE #RecordDates" );
        }

        /// <summary>
        /// The Record Sources <see cref="FirstRecordsSql"/> names.
        /// </summary>
        private static readonly string[] FirstRecordSources = { "Check-in", "Group Registration", "Giving", "Serving Connection", "Event Registration", "Benevolence Request", "External Website", "Workflow" };

        /// <summary>
        /// The kinds of rows Rock writes for a person as things happen, so the person's record must already exist. Each
        /// gives a person Id, a time, and the Record Source of the form that writes it, or null for a note or a History
        /// line. Communication lists, prayer requests, emails, and History lines Rock dates by an earlier event are left
        /// out (see WriteRecordDates). Each person's earliest row is their first record. {0} is a condition on
        /// [r].[PersonId].
        /// </summary>
        internal const string FirstRecordsSql = @"
SELECT [f].[PersonId], [f].[At], [f].[Source]
FROM (
    SELECT [r].[PersonId], [r].[At], [r].[Source], ROW_NUMBER() OVER ( PARTITION BY [r].[PersonId] ORDER BY [r].[At], [r].[Source] ) AS [Row]
    FROM (
        SELECT [pa].[PersonId], [a].[StartDateTime] AS [At], 'Check-in' AS [Source] FROM [Attendance] AS [a] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [a].[PersonAliasId]
        UNION ALL SELECT [gm].[PersonId], [gm].[DateTimeAdded], 'Group Registration' FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
            WHERE [g].[IsSecurityRole] = 0 AND [gt].[Guid] NOT IN ( '790E3215-3B10-442B-AF69-616C0DCB998E', 'E0C5A0E2-B7B3-4EF4-820D-BBF7F9A374EF', 'D1D95777-FFA3-CBB3-4A6D-658706DAED33' )
        UNION ALL SELECT [pa].[PersonId], [t].[TransactionDateTime], 'Giving' FROM [FinancialTransaction] AS [t] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
        UNION ALL SELECT [pa].[PersonId], [r].[CreatedDateTime], 'Serving Connection' FROM [ConnectionRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
        UNION ALL SELECT [pa].[PersonId], [r].[CreatedDateTime], 'Event Registration' FROM [RegistrationRegistrant] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
        UNION ALL SELECT [pa].[PersonId], [r].[RequestDateTime], 'Benevolence Request' FROM [BenevolenceRequest] AS [r] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[RequestedByPersonAliasId]
        UNION ALL SELECT [n].[EntityId], [n].[CreatedDateTime], NULL FROM [Note] AS [n] INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId] WHERE [nt].[EntityTypeId] = @PersonType
        UNION ALL SELECT [u].[PersonId], [u].[CreatedDateTime], 'External Website' FROM [UserLogin] AS [u] WHERE [u].[PersonId] IS NOT NULL
        UNION ALL SELECT [w].[EntityId], [w].[ActivatedDateTime], 'Workflow' FROM [Workflow] AS [w] WHERE [w].[EntityTypeId] = @PersonType
        UNION ALL SELECT [h].[EntityId], [h].[CreatedDateTime], NULL FROM [History] AS [h] WHERE [h].[EntityTypeId] = @PersonType AND [h].[CategoryId] = @Demographics
    ) AS [r]
    WHERE [r].[At] IS NOT NULL AND {0}
) AS [f]
WHERE [f].[Row] = 1";

        /// <summary>
        /// Each listed person's first record time (see <see cref="FirstRecordsSql"/>), by person Id. A person with none is
        /// left out.
        /// </summary>
        internal static Dictionary<int, DateTime> FirstRecords( RockDatabase db, IEnumerable<int> personIds )
        {
            var ids = personIds.Distinct().ToList();
            return ids.Count == 0
                ? new Dictionary<int, DateTime>()
                : QueryFirstRecords( db, $"[r].[PersonId] IN ( {string.Join( ", ", ids )} )" ).ToDictionary( r => r.Key, r => r.Value.At );
        }

        /// <summary>
        /// Every person's first record in this dataset, by person Id.
        /// </summary>
        private static Dictionary<int, FirstRecord> FirstRecordsForDataset( RockDatabase db, string foreignKey )
        {
            return QueryFirstRecords( db, "[r].[PersonId] IN ( SELECT [Id] FROM [Person] WHERE [ForeignKey] = @ForeignKey OR [Id] = 1 )", P( "@ForeignKey", foreignKey ) );
        }

        private static Dictionary<int, FirstRecord> QueryFirstRecords( RockDatabase db, string condition, params Microsoft.Data.SqlClient.SqlParameter[] extra )
        {
            var parameters = new[] { P( "@PersonType", db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid ) ), P( "@Demographics", db.RequireIdByGuid( "Category", DemographicHistoryCategoryGuid ) ) }
                .Concat( extra ).ToArray();
            return db.Query( string.Format( FirstRecordsSql, condition ), parameters )
                .ToDictionary( r => ( int ) r["PersonId"], r => new FirstRecord( ( DateTime ) r["At"], r["Source"] as string ) );
        }
    }
}
