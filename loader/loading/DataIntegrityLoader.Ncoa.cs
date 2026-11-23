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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    internal sealed partial class DataIntegrityLoader
    {
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );
        private static readonly Guid FamilyChangesCategoryGuid = new Guid( "5C4CCE5A-D7D0-492F-A241-96E13A3F7DF8" );
        private static readonly Guid GroupChangesCategoryGuid = new Guid( "089EB47D-D0EF-493E-B867-DC51BCDEF319" );
        private static readonly Guid HomeLocationTypeGuid = new Guid( "8C52E53C-2A66-435A-AE6E-5EE307D9A0DC" );
        private static readonly Guid PreviousLocationTypeGuid = new Guid( "853D98F1-6E08-4321-861B-520B4106CFE0" );
        private static readonly Guid FamilyGroupTypeGuid = new Guid( "790E3215-3B10-442B-AF69-616C0DCB998E" );
        private static readonly Guid PersonRecordTypeGuid = new Guid( "36CF10D6-C695-413D-8E7C-4546EFEF385E" );

        /// <summary>
        /// Rock's system setting for Spark Data, which holds the NCOA settings.
        /// </summary>
        internal const string SparkDataSettingKey = "core_SparkData";

        /// <summary>
        /// The SourceOfChange Rock's NCOA processing gives the History lines it writes.
        /// </summary>
        internal const string NcoaSourceOfChange = "NCOA Request";

        // Rock's NCOA enums (Rock.Model.NcoaType, MoveType, Processed, MatchFlag, AddressStatus, UpdatedAddressType).
        internal const int NcoaTypeNoMove = 1;
        internal const int NcoaTypeMove = 3;
        internal const int MoveTypeNone = 0;
        internal const int MoveTypeFamily = 1;
        internal const int MoveTypeIndividual = 2;
        internal const int ProcessedComplete = 1;
        internal const int ProcessedManualUpdateRequired = 2;
        private const int MatchFlagNone = 0;
        private const int MatchFlagMoved = 1;
        private const int AddressStatusValid = 1;
        private const int UpdatedAddressTypeResidential = 1;

        private const double MilesPerDegree = 69.0;

        /// <summary>
        /// A person Rock sent to NCOA, with the family and home Rock sent with them.
        /// </summary>
        private sealed record NcoaPerson( int PersonId, int AliasId, int FamilyId, int LocationId, int GroupLocationId, bool IsMapped, string Street1, string Street2, string City, string State,
            string PostalCode, double? Latitude, double? Longitude, DateTime? BirthDate );

        /// <summary>
        /// A move the run found: a family's, or one person's when <paramref name="PersonId"/> is set.
        /// </summary>
        private sealed record NcoaMove( int FamilyId, int? PersonId, CastAddress To, decimal Distance, bool IsAway, bool IsProcessed, int MoveType );

        /*
            11/23/26 - CLAUDE

            A church runs NCOA from Rock's NCOA page: it sends its addresses to TrueNCOA and imports the results file.
            Rock keeps one NCOA History row per person it sent (Ncoa.PendingExport), and processes the moves
            (Ncoa.ProcessNcoaResults). A family that moved gets its new home: the old one becomes Previous, the new one is
            the Home and mailing address, and each family member gets two History lines with the source NCOA Request.
            Rock's GroupLocation save hook adds the family's own History. A family that moved at least the minimum
            distance is also inactivated with the page's reason and Rock's note. An individual who moved out of a family
            of two or more is left for a staff member, and the NCOA Results page lists them. Rock's move away check also
            measures campuses, but compares meters to miles, so every move past the minimum counts. The new homes here are
            far from every campus too, so both readings agree.

            The run is written as Rock leaves it: the rows, the settings the page saved, and each processed move.

            Reason: Rock's NCOA Results page and the families it moved show a real run (issue 2 slice 3, decision
            D-integrity-13).
        */

        /// <summary>
        /// Writes one NCOA run: the settings, a History row for each person Rock sent, and each move as Rock processed it.
        /// </summary>
        private List<string> WriteNcoa( NcoaRun ncoa, IReadOnlyCollection<LoadedHousehold> households, IReadOnlyCollection<string> castHouseholdKeys, PeopleLoader people )
        {
            if ( ncoa?.Run == null )
            {
                return new List<string> { "  NCOA: none in this package" };
            }

            var run = At( ncoa.Run );
            var byAliasId = people.People.TryGetValue( ncoa.ProcessedBy, out var processedBy ) ? processedBy.AliasId
                : throw new InvalidDataException( $"configuration/data-integrity.json's NCOA run names {ncoa.ProcessedBy}, who is not in this dataset." );
            var reasonId = Convert.ToInt32( _db.Scalar( "SELECT TOP 1 [Id] FROM [DefinedValue] WHERE [Value] = @Name AND [DefinedTypeId] = ( SELECT [DefinedTypeId] FROM [DefinedValue] WHERE [Guid] = @Deceased )",
                P( "@Name", ncoa.InactiveReason ), P( "@Deceased", DeceasedReasonGuid ) ) ?? throw new InvalidDataException( $"The NCOA reason \"{ncoa.InactiveReason}\" is not one of Rock's Record Status Reasons." ) );
            var dataViewId = _db.IdByGuid( "DataView", ncoa.PersonDataView ) ?? throw new InvalidDataException( $"The NCOA run's data view {ncoa.PersonDataView} is not in this database." );
            SaveSystemSetting( SparkDataSettingKey, SparkDataJson( ncoa, run, dataViewId, reasonId ) );

            var castMoves = ncoa.Cast.Select( m => (Move: m, Household: households.FirstOrDefault( h => h.Cast.Key == m.Household )) ).Where( m => m.Household != null ).ToList();
            var exported = NcoaExport( run, castMoves.Where( m => m.Move.Move == "away" ).SelectMany( m => m.Household.Members ).Select( p => p.PersonId ) );
            var families = exported.GroupBy( p => p.FamilyId ).ToDictionary( g => g.Key, g => g.ToList() );
            var householdByFamily = households.GroupBy( h => h.FamilyId ).ToDictionary( g => g.Key, g => g.First() );
            var addresses = new NcoaAddresses( _source, _db );
            var moves = new Dictionary<int, NcoaMove>();

            foreach ( var (move, household) in castMoves )
            {
                if ( !families.TryGetValue( household.FamilyId, out var sent ) )
                {
                    throw new InvalidDataException( $"The NCOA run moves {household.Cast.Name}, but Rock would not have sent them: no active member with a home address before the run." );
                }

                var person = move.Person.HasValue ? sent.FirstOrDefault( p => people.People.TryGetValue( move.Person.Value, out var cast ) && cast.PersonId == p.PersonId ) : null;
                var planned = PlanMove( move.Move, sent, person, household, addresses, ncoa.MinimumMoveDistance );
                moves[household.FamilyId] = planned ?? throw new InvalidDataException( $"The NCOA run has no address left for {household.Cast.Name}'s {move.Move} move." );
            }

            // The Crowd: families in a seeded order, the moves away first, from the families that qualify.
            var generated = families.Keys.Where( id => householdByFamily.TryGetValue( id, out var h ) && !castHouseholdKeys.Contains( h.Cast.Key ) )
                .OrderBy( id => SeededRandom.StableGuid( $"data-integrity:ncoa:{householdByFamily[id].Cast.Guid}" ) ).ToList();
            int Share( double pct ) => ( int ) Math.Round( pct / 100 * generated.Count );
            var canMoveAway = FamiliesThatCanMoveAway( generated, run );
            var skipped = 0;
            void Plan( IEnumerable<int> candidates, int count, string kind )
            {
                foreach ( var familyId in candidates.Where( id => !moves.ContainsKey( id ) ).Take( count ) )
                {
                    var sent = families[familyId];
                    var person = kind == "individual" ? MovedOutAdult( sent ) : null;
                    var planned = PlanMove( kind, sent, person, householdByFamily[familyId], addresses, ncoa.MinimumMoveDistance );
                    if ( planned == null )
                    {
                        skipped++;
                        continue;
                    }

                    moves[familyId] = planned;
                }
            }

            Plan( generated.Where( canMoveAway.Contains ), Share( ncoa.Crowd.MovedAwayFamiliesPct ), "away" );
            Plan( generated.Where( id => MovedOutAdult( families[id] ) != null ), Share( ncoa.Crowd.AdultsMovedOutPct ), "individual" );
            Plan( generated, Share( ncoa.Crowd.NearbyFamiliesPct ), "nearby" );

            WriteNcoaHistory( exported, moves, run, byAliasId );
            var history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int", "Verb:string",
                "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "OldRawValue:string", "NewRawValue:string", "IsSensitive:bool", "SourceOfChange:string", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var inactivated = 0;
            foreach ( var move in moves.Values.Where( m => m.IsProcessed ) )
            {
                inactivated += ApplyMove( move, families[move.FamilyId], run, byAliasId, reasonId, ncoa.MinimumMoveDistance, history );
            }

            _db.BulkInsert( "History", history, keepIdentity: false );

            var kinds = moves.Values.GroupBy( m => !m.IsProcessed ? "waiting for a staff member" : m.IsAway ? "moved away" : "moved nearby" ).ToDictionary( g => g.Key, g => g.Count() );
            return new List<string>
            {
                $"  NCOA run {run:yyyy-MM-dd HH:mm}: {exported.Count:N0} people sent in {families.Count:N0} families; {kinds.GetValueOrDefault( "moved nearby" ):N0} families moved nearby, "
                    + $"{kinds.GetValueOrDefault( "moved away" ):N0} moved away ({inactivated:N0} people inactivated), {kinds.GetValueOrDefault( "waiting for a staff member" ):N0} individual moves wait for a staff member"
                    + ( skipped > 0 ? $"; {skipped:N0} moves skipped with no address left" : string.Empty )
            };
        }

        /// <summary>
        /// The NCOA settings Rock's NCOA page saved with the run, as Rock's SparkDataConfig serializes them.
        /// </summary>
        private static string SparkDataJson( NcoaRun ncoa, DateTime run, int dataViewId, int reasonId )
        {
            var settings = new JsonObject
            {
                ["IsEnabled"] = false,
                ["PersonFullName"] = null,
                ["LastRunDate"] = run.ToString( "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture ),
                ["RecurringEnabled"] = true,
                ["RecurrenceInterval"] = ncoa.RecurrenceInterval,
                ["FileName"] = null,
                ["CurrentReportKey"] = string.Empty,
                ["CurrentReportExportKey"] = null,
                ["CurrentReportStatus"] = "Complete",
                ["PersonDataViewId"] = dataViewId,
                ["CurrentUploadCount"] = null,
                ["IsAcceptedTerms"] = false,
                ["IsAckPrice"] = false,
                ["InactiveRecordReasonId"] = reasonId,
                ["UploadFileReference"] = null,
                ["MinimumMoveDistance"] = JsonValue.Create( decimal.Parse( ncoa.MinimumMoveDistance.ToString( "0.0###", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture ) ),
                ["Is48MonthMoveChecked"] = ncoa.Mark48MonthAsPrevious,
                ["IsInvalidAddressesChecked"] = ncoa.MarkInvalidAsPrevious
            };

            return new JsonObject
            {
                ["NcoaSettings"] = settings,
                ["SparkDataApiKey"] = string.Empty,
                ["GlobalNotificationApplicationGroupId"] = null,
                ["Messages"] = new JsonArray()
            }.ToJsonString( new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping } );
        }

        /// <summary>
        /// Everyone Rock's NCOA page would have sent at the run, as Ncoa.GetAddresses reads them: each member of a family
        /// with a home address who was not inactive, in the run's data view, once. The data view is Active People, so a
        /// record that was active then counts: active now, or inactivated after the run. Records made after the run and
        /// businesses are left out. <paramref name="alsoActiveThen"/> are people this run inactivates.
        /// </summary>
        private List<NcoaPerson> NcoaExport( DateTime run, IEnumerable<int> alsoActiveThen )
        {
            var also = alsoActiveThen.ToList();
            return _db.Query( $@"
SELECT [p].[Id] AS [PersonId], ( SELECT MIN( [pa].[Id] ) FROM [PersonAlias] AS [pa] WHERE [pa].[PersonId] = [p].[Id] ) AS [AliasId], [gm].[GroupId] AS [FamilyId],
    [home].[LocationId], [home].[Id] AS [GroupLocationId], [home].[IsMappedLocation], [l].[Street1], [l].[Street2], [l].[City], [l].[State], [l].[PostalCode],
    [l].[GeoPoint].Lat AS [Latitude], [l].[GeoPoint].Long AS [Longitude], [p].[BirthDate]
FROM [Person] AS [p]
CROSS APPLY ( SELECT TOP 1 [m].[GroupId] FROM [GroupMember] AS [m] INNER JOIN [Group] AS [g] ON [g].[Id] = [m].[GroupId]
    WHERE [m].[PersonId] = [p].[Id] AND [g].[GroupTypeId] = @FamilyType ORDER BY [m].[Id] ) AS [gm]
CROSS APPLY ( SELECT TOP 1 [gl].[Id], [gl].[LocationId], [gl].[IsMappedLocation] FROM [GroupLocation] AS [gl]
    WHERE [gl].[GroupId] = [gm].[GroupId] AND [gl].[GroupLocationTypeValueId] = @Home ORDER BY [gl].[Id] ) AS [home]
INNER JOIN [Location] AS [l] ON [l].[Id] = [home].[LocationId]
WHERE [p].[ForeignKey] = @ForeignKey AND [p].[RecordTypeValueId] = @PersonRecord AND [p].[CreatedDateTime] < @Run
    AND ISNULL( [l].[Street1], '' ) <> '' AND ISNULL( [l].[State], '' ) <> ''
    AND ( [p].[RecordStatusValueId] = @Active OR ( [p].[RecordStatusValueId] = @Inactive AND [p].[RecordStatusLastModifiedDateTime] >= @Run ){( also.Count > 0 ? $" OR [p].[Id] IN ( {string.Join( ", ", also )} )" : string.Empty )} )
ORDER BY [gm].[GroupId], [p].[Id]",
                    P( "@FamilyType", _db.RequireIdByGuid( "GroupType", FamilyGroupTypeGuid ) ), P( "@Home", _db.RequireIdByGuid( "DefinedValue", HomeLocationTypeGuid ) ),
                    P( "@ForeignKey", _foreignKey ), P( "@PersonRecord", _db.RequireIdByGuid( "DefinedValue", PersonRecordTypeGuid ) ), P( "@Run", run ),
                    P( "@Active", _db.RequireIdByGuid( "DefinedValue", ActiveStatusGuid ) ), P( "@Inactive", _db.RequireIdByGuid( "DefinedValue", InactiveStatusGuid ) ) )
                .Select( r => new NcoaPerson( ( int ) r["PersonId"], ( int ) r["AliasId"], ( int ) r["FamilyId"], ( int ) r["LocationId"], ( int ) r["GroupLocationId"], ( bool ) r["IsMappedLocation"],
                    ( string ) r["Street1"], r["Street2"] as string, r["City"] as string, ( string ) r["State"], r["PostalCode"] as string, r["Latitude"] as double?, r["Longitude"] as double?,
                    r["BirthDate"] as DateTime? ) )
                .ToList();
        }

        /// <summary>
        /// The families that can move away with nothing else changing: no check-in or gift in the year before the run or
        /// since, and no group membership, open connection request, or communication flow that Rock's person save hook
        /// would inactivate.
        /// </summary>
        private HashSet<int> FamiliesThatCanMoveAway( List<int> familyIds, DateTime run )
        {
            var rows = TableFactory.Create( "Id:int" );
            foreach ( var id in familyIds )
            {
                rows.Rows.Add( id );
            }

            _db.Execute( "CREATE TABLE #NcoaFamilies ( [Id] INT NOT NULL PRIMARY KEY )" );
            _db.BulkInsert( "#NcoaFamilies", rows, keepIdentity: false );
            var eligible = _db.Query( @"
SELECT [f].[Id]
FROM #NcoaFamilies AS [f]
WHERE NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [fm] INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [fm].[PersonId]
        INNER JOIN [Attendance] AS [a] ON [a].[PersonAliasId] = [pa].[Id]
        WHERE [fm].[GroupId] = [f].[Id] AND [a].[DidAttend] = 1 AND [a].[StartDateTime] >= @YearBefore )
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [fm] INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [fm].[PersonId]
        INNER JOIN [FinancialTransaction] AS [t] ON [t].[AuthorizedPersonAliasId] = [pa].[Id]
        WHERE [fm].[GroupId] = [f].[Id] AND [t].[TransactionDateTime] >= @YearBefore )
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [fm] INNER JOIN [GroupMember] AS [m] ON [m].[PersonId] = [fm].[PersonId]
        INNER JOIN [Group] AS [g] ON [g].[Id] = [m].[GroupId] INNER JOIN [GroupType] AS [gt] ON [gt].[Id] = [g].[GroupTypeId]
        WHERE [fm].[GroupId] = [f].[Id] AND [m].[GroupMemberStatus] <> 0 AND [gt].[IgnorePersonInactivated] = 0 )
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [fm] INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [fm].[PersonId]
        INNER JOIN [ConnectionRequest] AS [r] ON [r].[PersonAliasId] = [pa].[Id]
        WHERE [fm].[GroupId] = [f].[Id] AND [r].[ConnectionState] NOT IN ( 1, 3 ) )
    AND NOT EXISTS ( SELECT 1 FROM [GroupMember] AS [fm] INNER JOIN [PersonAlias] AS [pa] ON [pa].[PersonId] = [fm].[PersonId]
        INNER JOIN [CommunicationFlowInstanceRecipient] AS [r] ON [r].[RecipientPersonAliasId] = [pa].[Id]
        WHERE [fm].[GroupId] = [f].[Id] AND [r].[Status] = 1 )", P( "@YearBefore", run.AddYears( -1 ) ) )
                .Select( r => ( int ) r["Id"] ).ToHashSet();
            _db.Execute( "DROP TABLE #NcoaFamilies" );
            return eligible;
        }

        /// <summary>
        /// The adult who moved out of a family Rock sent: an adult child, 18 to 29 with someone in the family at least 18
        /// years older, or else the youngest of two or more adults. Null when the family has no such adult.
        /// </summary>
        private NcoaPerson MovedOutAdult( List<NcoaPerson> sent )
        {
            var adults = sent.Where( p => p.BirthDate.HasValue && _dates.AgeOn( p.BirthDate.Value ) >= 18 ).OrderBy( p => p.BirthDate.Value ).ToList();
            var child = adults.FirstOrDefault( p => _dates.AgeOn( p.BirthDate.Value ) <= 29 && adults.Any( o => o.BirthDate.Value.AddYears( 18 ) <= p.BirthDate.Value ) );
            return child ?? ( adults.Count >= 2 ? adults[adults.Count - 1] : null );
        }

        /// <summary>
        /// Plans one move with its new home, or null when no address fits. A nearby move or an individual one stays near
        /// the family's campus and under the minimum distance. A move away goes to the pool's far list, at least the
        /// minimum distance from the old home.
        /// </summary>
        private NcoaMove PlanMove( string kind, List<NcoaPerson> sent, NcoaPerson person, LoadedHousehold household, NcoaAddresses addresses, decimal minimum )
        {
            var from = sent[0];
            if ( kind == "individual" && person == null )
            {
                throw new InvalidDataException( $"The NCOA run moves one person out of {household.Cast.Name}, but Rock did not send that person." );
            }

            var isAway = kind == "away";
            var to = isAway
                ? addresses.Take( null, household.Cast.Guid, a => Miles( from, a ) >= ( double ) minimum )
                : addresses.Take( household.CampusKey, household.Cast.Guid, a =>
                {
                    var miles = Miles( from, a );
                    return miles >= 1 && miles < ( double ) minimum;
                } );
            if ( to == null )
            {
                return null;
            }

            var distance = Math.Round( ( decimal ) Miles( from, to ), 2 );
            if ( kind == "individual" )
            {
                // Rock processes an individual move only when the person is the whole family.
                var isAlone = FamilySize( household.FamilyId ) == 1;
                return new NcoaMove( household.FamilyId, person.PersonId, to, distance, false, isAlone, MoveTypeIndividual );
            }

            return new NcoaMove( household.FamilyId, null, to, distance, isAway, true, FamilySize( household.FamilyId ) == 1 ? MoveTypeIndividual : MoveTypeFamily );
        }

        private int FamilySize( int familyId ) => Convert.ToInt32( _db.Scalar( "SELECT COUNT(*) FROM [GroupMember] WHERE [GroupId] = @Id", P( "@Id", familyId ) ) );

        private static double Miles( NcoaPerson from, CastAddress to )
        {
            if ( !from.Latitude.HasValue || !from.Longitude.HasValue || !to.Latitude.HasValue || !to.Longitude.HasValue )
            {
                return double.NaN;
            }

            var dLat = ( from.Latitude.Value - to.Latitude.Value ) * MilesPerDegree;
            var dLon = ( from.Longitude.Value - to.Longitude.Value ) * MilesPerDegree * Math.Cos( from.Latitude.Value * Math.PI / 180 );
            return Math.Sqrt( ( dLat * dLat ) + ( dLon * dLon ) );
        }

        /// <summary>
        /// The NCOA History rows: one per person sent, as Rock's import leaves them.
        /// </summary>
        private void WriteNcoaHistory( List<NcoaPerson> exported, Dictionary<int, NcoaMove> moves, DateTime run, int byAliasId )
        {
            var rows = TableFactory.Create( "PersonAliasId:int", "FamilyId:int", "LocationId:int", "MoveType:int", "NcoaType:int", "AddressStatus:int", "AddressInvalidReason:int",
                "OriginalStreet1:string", "OriginalStreet2:string", "OriginalCity:string", "OriginalState:string", "OriginalPostalCode:string",
                "UpdatedStreet1:string", "UpdatedStreet2:string", "UpdatedCity:string", "UpdatedState:string", "UpdatedPostalCode:string", "UpdatedCountry:string", "UpdatedBarcode:string",
                "UpdatedAddressType:int", "MoveDate:date", "MoveDistance:decimal", "MatchFlag:int", "Processed:int", "NcoaRunDateTime:date", "NcoaNote:string",
                "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string" );
            foreach ( var person in exported )
            {
                moves.TryGetValue( person.FamilyId, out var move );
                var moved = move != null && ( move.PersonId == null || move.PersonId == person.PersonId );
                var street1 = moved ? move.To.Street1 : person.Street1;
                var city = moved ? move.To.City : person.City;
                var postal = moved ? move.To.PostalCode : person.PostalCode;
                rows.Rows.Add( person.AliasId, person.FamilyId, person.LocationId, moved ? move.MoveType : MoveTypeNone, moved ? NcoaTypeMove : NcoaTypeNoMove, AddressStatusValid, 0,
                    person.Street1, Text( person.Street2 ), Text( person.City ), person.State, Text( person.PostalCode ),
                    street1, moved ? DBNull.Value : Text( person.Street2 ), Text( city ), moved ? move.To.State : person.State, Text( postal ), "US", Barcode( postal, street1 ),
                    UpdatedAddressTypeResidential, DBNull.Value, moved ? move.Distance : ( object ) DBNull.Value, moved ? MatchFlagMoved : MatchFlagNone,
                    moved && !move.IsProcessed ? ProcessedManualUpdateRequired : ProcessedComplete, run, DBNull.Value,
                    run, run, byAliasId, byAliasId, SeededRandom.StableGuid( $"data-integrity:ncoa-history:{_foreignKey}:{person.PersonId}" ), _foreignKey );
            }

            _db.BulkInsert( "NcoaHistory", rows, keepIdentity: false );
        }

        /// <summary>
        /// A made-up delivery point barcode: the ZIP code, four more digits, a delivery point, and a check digit.
        /// </summary>
        private static string Barcode( string postalCode, string street1 )
        {
            var random = new SeededRandom( $"data-integrity:ncoa-barcode:{postalCode}:{street1}" );
            var digits = $"{postalCode}{random.Between( 1, 9999 ):0000}{random.Between( 0, 99 ):00}";
            var sum = digits.Sum( c => c - '0' );
            return digits + ( ( 10 - ( sum % 10 ) ) % 10 ).ToString( CultureInfo.InvariantCulture );
        }

        /// <summary>
        /// Applies one processed move as Rock does: the old home becomes Previous, a new Home is added, each family member
        /// gets Rock's NCOA History lines, and a family that moved away is inactivated. Returns how many it inactivated.
        /// </summary>
        private int ApplyMove( NcoaMove move, List<NcoaPerson> sent, DateTime run, int byAliasId, int reasonId, decimal minimum, DataTable history )
        {
            var old = sent[0];
            var oldText = LocationText( old.Street1, old.Street2, old.City, old.State, old.PostalCode );
            var newText = LocationText( move.To.Street1, null, move.To.City, move.To.State, move.To.PostalCode );
            var locationGuid = SeededRandom.StableGuid( $"data-integrity:ncoa-location:{_foreignKey}:{move.FamilyId}" );
            _db.Execute( $@"
INSERT INTO [Location] ( [Street1], [City], [State], [PostalCode], [Country], [IsActive], [IsGeoPointLocked], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @Street1, @City, @State, @PostalCode, 'US', 1, 0, @Guid, @At, @At, @By, @By, @ForeignKey );
UPDATE [Location] SET [GeoPoint] = geography::Point( @Latitude, @Longitude, 4326 ), {CheckedAddress.SetSql} WHERE [Guid] = @Guid;",
                new[] { P( "@Street1", move.To.Street1 ), P( "@City", move.To.City ), P( "@State", move.To.State ), P( "@PostalCode", move.To.PostalCode ), P( "@Guid", locationGuid ), P( "@At", run ),
                    P( "@By", byAliasId ), P( "@ForeignKey", _foreignKey ), P( "@Latitude", move.To.Latitude.Value ), P( "@Longitude", move.To.Longitude.Value ) }.Concat( CheckedAddress.Parameters( run ) ).ToArray() );
            var locationId = _db.RequireIdByGuid( "Location", locationGuid );

            _db.Execute( "UPDATE [GroupLocation] SET [GroupLocationTypeValueId] = @Previous, [ModifiedDateTime] = @At, [ModifiedByPersonAliasId] = @By WHERE [Id] = @Id",
                P( "@Previous", _db.RequireIdByGuid( "DefinedValue", PreviousLocationTypeGuid ) ), P( "@At", run ), P( "@By", byAliasId ), P( "@Id", old.GroupLocationId ) );
            _db.Execute( @"
INSERT INTO [GroupLocation] ( [GroupId], [LocationId], [GroupLocationTypeValueId], [IsMailingLocation], [IsMappedLocation], [Order], [IsOverflowLocation], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @Family, @Location, @Home, 1, @IsMapped, 0, 0, @Guid, @At, @At, @By, @By, @ForeignKey )",
                P( "@Family", move.FamilyId ), P( "@Location", locationId ), P( "@Home", _db.RequireIdByGuid( "DefinedValue", HomeLocationTypeGuid ) ), P( "@IsMapped", old.IsMapped ),
                P( "@Guid", SeededRandom.StableGuid( $"data-integrity:ncoa-group-location:{_foreignKey}:{move.FamilyId}" ) ), P( "@At", run ), P( "@By", byAliasId ), P( "@ForeignKey", _foreignKey ) );

            // The family's History, from Rock's GroupLocation save hook: the old home changes type, and the new one is added.
            var groupTypeId = _db.RequireIdByGuid( "EntityType", GroupEntityTypeGuid );
            var groupCategoryId = _db.RequireIdByGuid( "Category", GroupChangesCategoryGuid );
            var line = 0;
            void GroupLine( string valueName, string oldValue, string newValue ) => history.Rows.Add( false, groupCategoryId, groupTypeId, move.FamilyId, DBNull.Value, DBNull.Value, DBNull.Value, ModifyVerb,
                PropertyChangeType, valueName, Text( oldValue ), Text( newValue ), DBNull.Value, DBNull.Value, false, DBNull.Value,
                SeededRandom.StableGuid( $"data-integrity:ncoa-history:{_foreignKey}:{move.FamilyId}:group:{line++}" ), run, run, byAliasId, byAliasId, _foreignKey );
            GroupLine( "Home Location", oldText, null );
            GroupLine( "Previous Location", null, oldText );
            GroupLine( "Home Location", null, newText );
            GroupLine( "Home Is Mailing", bool.FalseString, bool.TrueString );
            if ( old.IsMapped )
            {
                GroupLine( "Home Is Map Location", bool.FalseString, bool.TrueString );
            }

            // Each family member's History, from Rock's NCOA processing, and for a move away, the person save hook's lines.
            var members = _db.Query( @"
SELECT [p].[Id], [p].[NickName] + ' ' + [p].[LastName] AS [Name], [g].[Name] AS [FamilyName], [p].[RecordStatusValueId]
FROM [GroupMember] AS [gm] INNER JOIN [Person] AS [p] ON [p].[Id] = [gm].[PersonId] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
WHERE [gm].[GroupId] = @Family ORDER BY [gm].[Id]", P( "@Family", move.FamilyId ) ).ToList();
            var familyCategoryId = _db.RequireIdByGuid( "Category", FamilyChangesCategoryGuid );
            var activeId = _db.RequireIdByGuid( "DefinedValue", ActiveStatusGuid );
            var inactiveId = _db.RequireIdByGuid( "DefinedValue", InactiveStatusGuid );
            var note = $"Received a Change of Address (NCOA) notice that was for more than {minimum.ToString( CultureInfo.InvariantCulture )} miles away.";
            foreach ( var member in members )
            {
                var personId = ( int ) member["Id"];
                var name = ( string ) member["Name"];
                void PersonLine( int categoryId, string caption, object relatedType, object relatedId, string verb, string valueName, string oldValue, string newValue, object oldRaw, object newRaw,
                    object source, string key ) => history.Rows.Add( false, categoryId, _personTypeId, personId, caption, relatedType, relatedId, verb, PropertyChangeType, valueName, Text( oldValue ),
                    Text( newValue ), oldRaw, newRaw, false, source, SeededRandom.StableGuid( $"data-integrity:ncoa-history:{_foreignKey}:{personId}:{key}" ), run, run, byAliasId, byAliasId, _foreignKey );

                if ( move.IsAway )
                {
                    _db.Execute( @"
UPDATE [Person]
SET [RecordStatusValueId] = @Inactive, [RecordStatusReasonValueId] = @Reason, [InactiveReasonNote] = @Note, [RecordStatusLastModifiedDateTime] = @At, [ModifiedDateTime] = @At, [ModifiedByPersonAliasId] = @By
WHERE [Id] = @Id",
                        P( "@Inactive", inactiveId ), P( "@Reason", reasonId ), P( "@Note", note ), P( "@At", run ), P( "@By", byAliasId ), P( "@Id", personId ) );
                    var reasonName = ( string ) _db.Scalar( "SELECT [Value] FROM [DefinedValue] WHERE [Id] = @Id", P( "@Id", reasonId ) );
                    PersonLine( _historyCategoryId, name, DBNull.Value, DBNull.Value, ModifyVerb, "Record Status", "Active", "Inactive", activeId.ToString( CultureInfo.InvariantCulture ),
                        inactiveId.ToString( CultureInfo.InvariantCulture ), DBNull.Value, "status" );
                    PersonLine( _historyCategoryId, name, DBNull.Value, DBNull.Value, ModifyVerb, "Record Status Reason", null, reasonName, DBNull.Value, DBNull.Value, DBNull.Value, "reason" );
                    PersonLine( _historyCategoryId, name, DBNull.Value, DBNull.Value, ModifyVerb, "Inactive Reason Note", null, note, DBNull.Value, DBNull.Value, DBNull.Value, "note" );
                }

                var familyName = ( string ) member["FamilyName"];
                PersonLine( familyCategoryId, familyName, groupTypeId, move.FamilyId, ModifyVerb, $"Location Type for {oldText} ", null, "Previous", DBNull.Value, DBNull.Value, NcoaSourceOfChange, "previous" );
                PersonLine( familyCategoryId, familyName, groupTypeId, move.FamilyId, "ADD", "Location", null, newText, DBNull.Value, DBNull.Value, NcoaSourceOfChange, "location" );
            }

            return move.IsAway ? members.Count : 0;
        }

        /// <summary>
        /// A Location as Rock's Location.ToString writes it with the US address format: the street lines and the city
        /// line, with CRLF between them.
        /// </summary>
        internal static string LocationText( string street1, string street2, string city, string state, string postalCode )
        {
            var lines = new[] { street1, street2, $"{city}, {state} {postalCode}" }.Where( l => !string.IsNullOrWhiteSpace( l ) );
            return string.Join( "\r\n", lines );
        }

        /// <summary>
        /// Saves a system setting as Rock's SystemSettings.SetValue does: the value in the default value of a text
        /// attribute qualified as a system setting.
        /// </summary>
        private void SaveSystemSetting( string key, string value )
        {
            var updated = _db.Execute( "UPDATE [Attribute] SET [DefaultValue] = @Value, [ModifiedDateTime] = @Now WHERE [EntityTypeId] IS NULL AND [EntityTypeQualifierColumn] = 'SystemSetting' AND [Key] = @Key",
                P( "@Value", value ), P( "@Now", _dates.Anchor ), P( "@Key", key ) );
            if ( updated == 0 )
            {
                var name = Regex.Replace( Regex.Replace( key, @"(\P{Ll})(\P{Ll}\p{Ll})", "$1 $2" ), @"(\p{Ll})(\P{Ll})", "$1 $2" );
                _db.Execute( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( 0, @FieldType, 'SystemSetting', '', @Key, @Name, 0, 0, @Value, 0, 0, @Guid, @Now, @Now, @ForeignKey )",
                    P( "@FieldType", _db.RequireIdByGuid( "FieldType", TextFieldTypeGuid ) ), P( "@Key", key ), P( "@Name", name ), P( "@Value", value ),
                    P( "@Guid", SeededRandom.StableGuid( $"data-integrity:setting:{key}" ) ), P( "@Now", _dates.Anchor ), P( "@ForeignKey", _foreignKey ) );
            }
        }

        /// <summary>
        /// The address pool's homes no Location uses yet, by campus, and its far list.
        /// </summary>
        private sealed class NcoaAddresses
        {
            private readonly Dictionary<string, List<CastAddress>> _byCampus = new Dictionary<string, List<CastAddress>>();
            private readonly List<CastAddress> _far = new List<CastAddress>();
            private readonly HashSet<string> _used;

            public NcoaAddresses( SampleDataSource source, RockDatabase db )
            {
                _used = db.Query( "SELECT DISTINCT [Street1], [PostalCode] FROM [Location] WHERE [Street1] IS NOT NULL" )
                    .Select( r => $"{r["Street1"]}|{r["PostalCode"]}" ).ToHashSet( StringComparer.OrdinalIgnoreCase );
                if ( !source.Files.Exists( "addresses/pool.json" ) )
                {
                    return;
                }

                using var document = JsonDocument.Parse( source.Files.ReadText( "addresses/pool.json" ) );
                static CastAddress Row( JsonElement row ) => new CastAddress
                {
                    Type = "home",
                    Street1 = row[0].GetString(),
                    City = row[1].GetString(),
                    State = "AZ",
                    PostalCode = row[2].GetString(),
                    Latitude = row[3].GetDouble(),
                    Longitude = row[4].GetDouble()
                };
                foreach ( var campus in document.RootElement.GetProperty( "campuses" ).EnumerateObject() )
                {
                    _byCampus[campus.Name] = campus.Value.EnumerateArray().Select( Row ).ToList();
                }

                if ( document.RootElement.TryGetProperty( "far", out var far ) )
                {
                    _far.AddRange( far.EnumerateArray().Select( Row ) );
                }
            }

            /// <summary>
            /// Takes an unused home that fits, in an order seeded by the household: from the campus's list, or the far
            /// list when <paramref name="campusKey"/> is null. Null when none fits.
            /// </summary>
            public CastAddress Take( string campusKey, Guid householdGuid, Func<CastAddress, bool> fits )
            {
                var list = campusKey == null ? _far : _byCampus.TryGetValue( campusKey, out var campus ) ? campus : new List<CastAddress>();
                var found = list.Where( a => !_used.Contains( $"{a.Street1}|{a.PostalCode}" ) )
                    .OrderBy( a => SeededRandom.StableGuid( $"data-integrity:ncoa-address:{householdGuid}:{a.Street1}|{a.PostalCode}" ) )
                    .FirstOrDefault( fits );
                if ( found != null )
                {
                    _used.Add( $"{found.Street1}|{found.PostalCode}" );
                }

                return found;
            }
        }
    }
}
