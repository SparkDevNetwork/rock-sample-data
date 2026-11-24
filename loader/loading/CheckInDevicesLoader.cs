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
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/23/26 - CLAUDE

        Every campus gets its own kids, student, and volunteer kiosks, each with a label printer beside it, from
        configuration/kiosks.json. Rock's shipped kiosk and printer are renamed into the first kids kiosk and printer at
        the main campus rather than left beside them, so a new install has no kiosk that belongs to no campus. Every
        address is in 192.0.2.0/24, which is reserved for documentation, so a label sent to one never reaches a real
        printer. A package without the file keeps Rock's one kiosk for every check-in, as before.

        Reason: A real church has kiosks at each campus, and each check-in should name one there (decision 360).
    */

    /// <summary>
    /// Adds each campus's check-in kiosks and their label printers, and gives each kiosk its rooms.
    /// </summary>
    internal sealed class CheckInDevicesLoader
    {
        private static readonly Guid KioskTypeGuid = new Guid( "BC809626-1389-4543-B8BB-6FAC79C27AFD" );
        private static readonly Guid PrinterTypeGuid = new Guid( "8284B128-E73B-4863-9FC2-43E6827B65E6" );

        /// <summary>
        /// Rock's PrintFrom.Server: the Rock server sends each label to the kiosk's printer.
        /// </summary>
        private const int PrintFromServer = 1;

        /// <summary>
        /// Rock's PrintTo.Kiosk: labels print on the kiosk's own printer, not one set on the room.
        /// </summary>
        private const int PrintToKiosk = 1;

        private readonly RockDatabase _db;
        private readonly KiosksConfiguration _config;
        private readonly Dictionary<string, CampusPlan> _campuses;
        private readonly Dictionary<string, int> _groupIds;
        private readonly string _foreignKey;

        public CheckInDevicesLoader( RockDatabase db, SampleDataSource source, ConfigurationLoader configuration, string foreignKey )
        {
            _db = db;
            _config = source.Kiosks;
            _campuses = configuration.CampusPlans;
            _groupIds = configuration.CheckInGroupIdsByKey;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Adds or updates every campus's kiosks and printers and returns them. Without configuration/kiosks.json, every
        /// check-in names the first check-in kiosk Rock has, if any.
        /// </summary>
        public CheckInDevices Load()
        {
            if ( _config == null )
            {
                var kiosk = _db.Scalar( @"
SELECT TOP 1 [d].[Id]
FROM [Device] AS [d]
INNER JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [d].[DeviceTypeValueId]
WHERE [dv].[Guid] = @KioskType
ORDER BY [d].[Id]", P( "@KioskType", KioskTypeGuid ) );
                return CheckInDevices.Single( kiosk == null ? null : new Kiosk( Convert.ToInt32( kiosk ), null ) );
            }

            var network = NetworkPrefix( _config.Network );
            var kioskTypeId = _db.RequireIdByGuid( "DefinedValue", KioskTypeGuid );
            var printerTypeId = _db.RequireIdByGuid( "DefinedValue", PrinterTypeGuid );
            var kinds = _config.Kinds.ToDictionary( k => k.Key );
            var devices = new CheckInDevices();
            var count = 0;

            foreach ( var campus in _config.Campuses.Where( c => _campuses.ContainsKey( c.Campus ) ) )
            {
                var plan = _campuses[campus.Campus];
                var campusName = Convert.ToString( _db.Scalar( "SELECT [Name] FROM [Campus] WHERE [Id] = @Id", P( "@Id", plan.CampusId ) ) );
                var buildingId = Convert.ToInt32( _db.Scalar( "SELECT [ParentLocationId] FROM [Location] WHERE [Id] = @Id", P( "@Id", plan.RoomIds.Values.First() ) ) );
                var placed = new CampusDevices();
                var address = campus.FirstAddress;

                Kiosk Add( KioskKind kind, int? number )
                {
                    var isRock = IsRock( _config, campus.Campus, kind.Key, number );
                    var printerGuid = isRock ? _config.Rock.PrinterGuid : SeededRandom.StableGuid( $"printer:{campus.Campus}:{kind.Key}:{number ?? 1}" );
                    var kioskGuid = KioskGuid( _config, campus.Campus, kind.Key, number );
                    var kioskAddress = Address( network, address++ );
                    var printerAddress = Address( network, address++ );
                    var kioskName = Name( kind.Name, campusName, number );

                    var printerId = Upsert( printerGuid, Name( kind.PrinterName, campusName, number ), $"Label printer beside {kioskName}.", printerTypeId, printerAddress, null, 0, PrintToKiosk, !isRock );
                    var kioskId = Upsert( kioskGuid, kioskName, kind.Description, kioskTypeId, kioskAddress, printerId, PrintFromServer, PrintToKiosk, !isRock );
                    SetLocations( kioskId, kind.Building ? new List<int> { buildingId } : Rooms( kind.Rooms, buildingId ) );
                    count += 2;
                    return new Kiosk( kioskId, kioskAddress );
                }

                for ( var number = 1; number <= campus.KidsKiosks; number++ )
                {
                    placed.Kids.Add( Add( kinds["kids"], number ) );
                }

                placed.Students = Add( kinds["students"], null );
                placed.Volunteers = Add( kinds["volunteers"], null );
                devices.Campuses[campus.Campus] = placed;
            }

            _db.Log.Write( $"  Check-in kiosks and printers: {count} across {devices.Campuses.Count} campus(es)" );
            return devices;
        }

        /// <summary>
        /// Checks configuration/kiosks.json against checkin.json and campuses.json. The builder runs it, so a package
        /// never holds a kiosk with a room that does not exist or an address outside the reserved block.
        /// </summary>
        public static void Validate( KiosksConfiguration config, CheckInConfiguration checkIn, IEnumerable<string> campusKeys )
        {
            if ( config == null )
            {
                return;
            }

            var network = NetworkPrefix( config.Network );
            var groups = checkIn.Groups.Select( g => g.Key ).ToHashSet();
            var campuses = campusKeys.ToHashSet();
            foreach ( var key in new[] { "kids", "students", "volunteers" } )
            {
                var kind = config.Kinds.SingleOrDefault( k => k.Key == key ) ?? throw new InvalidDataException( $"configuration/kiosks.json has no \"{key}\" kiosk." );
                var unknown = kind.Rooms.FirstOrDefault( r => !groups.Contains( r ) );
                if ( unknown != null || ( !kind.Building && kind.Rooms.Count == 0 ) )
                {
                    throw new InvalidDataException( $"configuration/kiosks.json: the {key} kiosk needs rooms that are check-in groups in checkin.json, or the building." );
                }
            }

            if ( config.Rock != null && !config.Campuses.Any( c => c.Campus == config.Rock.Campus ) )
            {
                throw new InvalidDataException( $"configuration/kiosks.json gives Rock's kiosk to {config.Rock.Campus}, which has no kiosks." );
            }

            var used = new HashSet<int>();
            foreach ( var campus in config.Campuses )
            {
                if ( !campuses.Contains( campus.Campus ) || campus.KidsKiosks < 2 )
                {
                    throw new InvalidDataException( $"configuration/kiosks.json: {campus.Campus} must be a campus in campuses.json with at least two kids kiosks." );
                }

                var devices = ( campus.KidsKiosks + 2 ) * 2;
                for ( var address = campus.FirstAddress; address < campus.FirstAddress + devices; address++ )
                {
                    if ( address < 1 || address > 254 || !used.Add( address ) )
                    {
                        throw new InvalidDataException( $"configuration/kiosks.json: {campus.Campus}'s devices need addresses {network}{campus.FirstAddress} to {network}{campus.FirstAddress + devices - 1}, and {network}{address} is outside the block or taken." );
                    }
                }
            }
        }

        /// <summary>
        /// The Guid of a campus's kiosk of one kind, such as its second kids kiosk. Rock's shipped kiosk keeps its own.
        /// </summary>
        public static Guid KioskGuid( KiosksConfiguration config, string campus, string kind, int? number ) =>
            IsRock( config, campus, kind, number ) ? config.Rock.KioskGuid : SeededRandom.StableGuid( $"kiosk:{campus}:{kind}:{number ?? 1}" );

        private static bool IsRock( KiosksConfiguration config, string campus, string kind, int? number ) =>
            config.Rock != null && config.Rock.Campus == campus && kind == "kids" && number == 1;

        /// <summary>
        /// Updates the device with this Guid, or adds it. Rock's own devices keep their ForeignKey.
        /// </summary>
        private int Upsert( Guid guid, string name, string description, int typeId, string address, int? printerId, int printFrom, int printTo, bool isOurs )
        {
            var parameters = new[]
            {
                P( "@Name", name ), P( "@Description", description ), P( "@TypeId", typeId ), P( "@Address", address ), P( "@PrinterId", printerId ),
                P( "@PrintFrom", printFrom ), P( "@PrintTo", printTo ), P( "@Guid", guid ), P( "@ForeignKey", isOurs ? _foreignKey : null )
            };

            var id = _db.IdByGuid( "Device", guid );
            if ( id != null )
            {
                _db.Execute( @"
UPDATE [Device]
SET [Name] = @Name, [Description] = @Description, [DeviceTypeValueId] = @TypeId, [IPAddress] = @Address, [PrinterDeviceId] = @PrinterId,
    [PrintFrom] = @PrintFrom, [PrintToOverride] = @PrintTo, [IsActive] = 1, [ModifiedDateTime] = GETDATE()
WHERE [Guid] = @Guid", parameters );
                return id.Value;
            }

            return Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Device] ( [Name], [Description], [DeviceTypeValueId], [IPAddress], [PrinterDeviceId], [PrintFrom], [PrintToOverride], [IsActive], [HasCamera], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @TypeId, @Address, @PrinterId, @PrintFrom, @PrintTo, 1, 0, @Guid, @ForeignKey, GETDATE(), GETDATE() )", parameters ) );
        }

        /// <summary>
        /// Every room in the building where these check-in groups meet, a second room for a large grade included.
        /// </summary>
        private List<int> Rooms( List<string> groupKeys, int buildingId )
        {
            var groups = groupKeys.Select( ( key, i ) => P( $"@Group{i}", _groupIds[key] ) ).ToList();
            return _db.Query( $@"
SELECT DISTINCT [gl].[LocationId]
FROM [GroupLocation] AS [gl]
INNER JOIN [Location] AS [l] ON [l].[Id] = [gl].[LocationId]
WHERE [l].[ParentLocationId] = @Building AND [gl].[GroupId] IN ( {string.Join( ", ", groups.Select( g => g.ParameterName ) )} )
ORDER BY [gl].[LocationId]", groups.Append( P( "@Building", buildingId ) ).ToArray() )
                .Select( r => Convert.ToInt32( r["LocationId"] ) )
                .ToList();
        }

        /// <summary>
        /// Replaces a kiosk's locations, which are the rooms it offers.
        /// </summary>
        private void SetLocations( int kioskId, List<int> locationIds )
        {
            _db.Execute( "DELETE FROM [DeviceLocation] WHERE [DeviceId] = @Id", P( "@Id", kioskId ) );
            foreach ( var locationId in locationIds.Distinct() )
            {
                _db.Execute( "INSERT INTO [DeviceLocation] ( [DeviceId], [LocationId] ) VALUES ( @DeviceId, @LocationId )", P( "@DeviceId", kioskId ), P( "@LocationId", locationId ) );
            }
        }

        private static string Name( string format, string campus, int? number ) => format.Replace( "{campus}", campus ).Replace( "{number}", number?.ToString() ?? string.Empty ).Trim();

        private static string Address( string prefix, int last ) => $"{prefix}{last}";

        /// <summary>
        /// "192.0.2." from "192.0.2.0/24". Only a /24 block is supported, so each address differs in its last part.
        /// </summary>
        private static string NetworkPrefix( string network )
        {
            var parts = ( network ?? string.Empty ).Split( '/' );
            var octets = parts[0].Split( '.' );
            if ( parts.Length != 2 || parts[1] != "24" || octets.Length != 4 || octets[3] != "0" )
            {
                throw new InvalidDataException( $"configuration/kiosks.json: network \"{network}\" must be a /24 block, such as 192.0.2.0/24." );
            }

            return $"{octets[0]}.{octets[1]}.{octets[2]}.";
        }
    }

    /// <summary>
    /// The check-in kiosks at each campus.
    /// </summary>
    internal sealed class CheckInDevices
    {
        /// <summary>
        /// Each campus's kiosks, by campus key.
        /// </summary>
        public Dictionary<string, CampusDevices> Campuses { get; } = new Dictionary<string, CampusDevices>();

        /// <summary>
        /// The one kiosk every check-in names when a package has no kiosks file, or null when Rock has none.
        /// </summary>
        private Kiosk _single;

        /// <summary>
        /// Every check-in names this kiosk, or none when it is null.
        /// </summary>
        public static CheckInDevices Single( Kiosk kiosk ) => new CheckInDevices { _single = kiosk };

        /// <summary>
        /// A campus's kids kiosk by its index, wrapping around, so a family keeps its usual kiosk.
        /// </summary>
        public Kiosk Kids( string campus, int index ) => Campuses.TryGetValue( campus, out var devices ) ? devices.Kids[index % devices.Kids.Count] : _single;

        /// <summary>
        /// How many kids kiosks a campus has, or 1 when it has Rock's one kiosk.
        /// </summary>
        public int KidsCount( string campus ) => Campuses.TryGetValue( campus, out var devices ) ? devices.Kids.Count : 1;

        public Kiosk Students( string campus ) => Campuses.TryGetValue( campus, out var devices ) ? devices.Students : _single;

        public Kiosk Volunteers( string campus ) => Campuses.TryGetValue( campus, out var devices ) ? devices.Volunteers : _single;
    }

    /// <summary>
    /// One campus's kiosks.
    /// </summary>
    internal sealed class CampusDevices
    {
        public List<Kiosk> Kids { get; } = new List<Kiosk>();

        public Kiosk Students { get; set; }

        public Kiosk Volunteers { get; set; }
    }

    /// <summary>
    /// A kiosk's Device Id and its address, which Rock saves on each check-in session.
    /// </summary>
    internal sealed record Kiosk( int Id, string Address );
}
