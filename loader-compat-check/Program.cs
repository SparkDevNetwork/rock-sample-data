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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

using RockSampleData.Loader;
using RockSampleData.Loader.Compat;
using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Packages;

/*
    The loader compatibility check. Every value below is a literal: the RFC test vectors come from
    RFC 6070 and RFC 5869, and the rest were computed by .NET 10's own methods. Run it for both targets:

      dotnet run --project loader-compat-check -f net10.0
      dotnet run --project loader-compat-check -f net472

    Both must pass, which proves the .NET Framework build gives the same bytes as the .NET 10 build.
    The package index checks read an index from before the card fields joined it and one with fields
    this loader does not know, so both builds stay able to read an older and a newer index. The last ones
    check the order of loader versions and which index entries this loader may install on a Rock.
    The published package checks open the configuration of every published release, saved in
    published/<tag>/, so a model change that would stop this loader from reading a package already on
    storage fails here first (decision 168). Add a folder for each release that publishes
    packages: git show <tag>:configuration/<file> for each JSON file.
    The index signature checks (decision D-signing-1) read signing/signed-index.json, which
    release/sign-index.js signed with a throwaway test key, so both builds read what the Node signer
    writes. The other index checks sign their index here with a key made for this run. Neither key is
    one the shipped loader trusts.
*/

// Rock's Encryption salt and HKDF label, as in loader/database/RockKeys.cs and Rock/Security/Encryption.cs.
var rockSalt = Encoding.ASCII.GetBytes( "rsduYVC2leenXKTLYLkO9qsWU95HGCvWlbXcBTjtrj5dBJ7RPeGYiw7U3lZE+LWkT+jGrLP9deRMc8sUHJtc/wu2l4vANBx5f+p1zpRwQ2bB/E6Ta8k7haPiTRc4wYhrmWMrg8VfQ4MhAsSlijIfT9u+DszEkB2ba2k0FIPMSWk=" );
var rockInfo = Encoding.UTF8.GetBytes( "Rock.Security.Encryption.v2" );
var rockKeyBytes = KeyDerivation.Pbkdf2( Encoding.UTF8.GetBytes( "compat-check-data-encryption-key" ), rockSalt, 1000, HashAlgorithmName.SHA1, 32 );
var keys = RockKeys.FromKeys( "0123456789ABCDEF0123456789abcdef0123456789ABCDEF0123456789ABCDEF", "compat-check-data-encryption-key" );
var random = new SeededRandom( "rock-sample-data:compat-check" );
var runSigner = ECDsa.Create( ECCurve.NamedCurves.nistP256 );
var runPoint = runSigner.ExportParameters( false ).Q;
var runKeys = new[] { new IndexSigningKey( "run", runPoint.X, runPoint.Y ) };
var nodeSignedIndex = File.ReadAllText( Path.Combine( SigningFolder(), "signed-index.json" ) );
var nodeTestKeys = IndexSignature.ParseKeys( File.ReadAllText( Path.Combine( SigningFolder(), "test-keys.json" ) ) );

var checks = new (string Name, string Actual, string Expected)[]
{
    ("PBKDF2-SHA1, RFC 6070 case 5",
        Hex( KeyDerivation.Pbkdf2( Encoding.ASCII.GetBytes( "passwordPASSWORDpassword" ), Encoding.ASCII.GetBytes( "saltSALTsaltSALTsaltSALTsaltSALTsalt" ), 4096, HashAlgorithmName.SHA1, 25 ) ),
        "3d2eec4fe41c849b80c8d83662c0e44a8b291a964cf2f07038"),
    ("HKDF-SHA256, RFC 5869 case 1",
        Hex( KeyDerivation.HkdfSha256( Repeat( 0x0b, 22 ), 42, Range( 0x00, 13 ), Range( 0xf0, 10 ) ) ),
        "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865"),
    ("HKDF-SHA256, RFC 5869 case 3 (no salt, no info)",
        Hex( KeyDerivation.HkdfSha256( Repeat( 0x0b, 22 ), 42, new byte[0], new byte[0] ) ),
        "8da4e775a563c18f715f802a063c5a31b8a11f5c5ee1879ec3454e5f3c738d2d9d201395faa4b61a96c8"),
    ("Rock's PBKDF2 key bytes",
        Hex( rockKeyBytes ),
        "3024e39db2890dd716dd0cd53e051b57b4d049ebcfe5c39bd3e38e55d3c636b9"),
    ("Rock's HKDF encryption and MAC keys",
        Hex( KeyDerivation.HkdfSha256( rockKeyBytes, 64, rockSalt, rockInfo ) ),
        "238c19d7746967f299dfa2b5efb0e7d2194624244e933a9d4183e3f0eb8ea34cd35630a083f13e0335e39940d0c7bdaf6655d1f8f1a755c904301ecd5209df88"),
    ("Rock's keyed hash of a bank account",
        keys.BankAccountHash( "391000017", "123456789" ),
        "yGJKpuUUTXTZdSmym2uQnzW7Ztk="),
    ("Rock's IdKey for Id 42",
        keys.IdKey( 42 ),
        "aXkQaZ3bLV"),
    ("Package checksum",
        SampleDataPackage.Sha256( Encoding.UTF8.GetBytes( "Rock Solid Church" ) ),
        "daec011e5abcd90075ff49f459105c269be737fad214aa268ea2de8089f40fe8"),
    ("Seeded random draws",
        string.Join( ",", Enumerable.Range( 0, 5 ).Select( _ => random.Between( 0, 1000000 ) ) ) + "," + BitConverter.DoubleToInt64Bits( random.Between( 0.0, 1.0 ) ),
        "420363,117252,926818,494383,790491,4604133140953978213"),
    ("Package index written before the card fields",
        Card( Signed( Index( "" ) ) ),
        "core-1||0|185|0|1|0"),
    ("Package index with the card fields and fields this loader does not know",
        Card( Signed( Index( "\"bestFor\": \"Tests.\", \"highlights\": [ \"Photos\", \" \", \"Giving\" ], \"households\": 87, \"typicalInstallSeconds\": 10, \"futureField\": { \"nested\": [ 1, 2 ] }," ) ) ),
        "core-1|Tests.|2|185|87|1|10"),
    ("Loader versions in order",
        string.Join( ",", new[] { "1.0.0", "0.1.0-alpha.13", "0.1.0", "0.1.0-alpha.9", "1.0.0-rc.1", "0.1.0-beta", "0.1.0-alpha.13+abc" }
            .OrderBy( v => v, Comparer<string>.Create( LoaderVersion.Compare ) ) ),
        "0.1.0-alpha.9,0.1.0-alpha.13,0.1.0-alpha.13+abc,0.1.0-beta,0.1.0,1.0.0-rc.1,1.0.0"),
    ("Rock major releases",
        $"{RockVersionInfo.MajorOf( "20.0.10" )}|{RockVersionInfo.MajorOf( "21" )}|{RockVersionInfo.MajorOf( "Rock 21" )?.ToString() ?? "none"}",
        "20|21|none"),
    ("Package index entry from before rockMajor, on Rock 21.0.5 and 20.0.10",
        Installable( Signed( Index( "" ) ) ),
        "21|none|True|False"),
    ("Package index entry for Rock 21 with a minimum loader this loader meets",
        Installable( Signed( Index( "\"rockMajor\": 21, \"minimumLoaderVersion\": \"0.1.0-alpha.1\"," ) ) ),
        "21|0.1.0-alpha.1|True|False"),
    ("Package index entry that needs a newer loader",
        Installable( Signed( Index( "\"rockMajor\": 21, \"minimumLoaderVersion\": \"99.0.0\"," ) ) ),
        "21|99.0.0|False|False"),
    ("Index signed by release/sign-index.js",
        Read( () => Entry( SampleDataPackageIndex.Parse( nodeSignedIndex, nodeTestKeys ) ) ),
        "core-1|21|0.1.0-alpha.14|daec011e5abc"),
    ("Spark's keys",
        string.Join( ",", IndexSignature.SparkKeys.Select( k => k.KeyId ) ),
        "sampledata-index-1,sampledata-index-2"),
    ("Spark's keys refuse an index a test key signed",
        Read( () => Installable( SampleDataPackageIndex.Parse( nodeSignedIndex ) ) ),
        "InvalidDataException|The package index is signed by key 'test', which this loader does not know. A newer loader may know it."),
    ("An unsigned index is refused",
        Read( () => Installable( SampleDataPackageIndex.Parse( Index( "" ), nodeTestKeys ) ) ),
        "InvalidDataException|The package index is not signed, so this loader can't trust it. A loader from 0.1.0-alpha.14 on reads only an index Spark signed."),
    ("A changed payload is refused",
        Read( () => Installable( SampleDataPackageIndex.Parse( Changed( nodeSignedIndex, signed => signed["payload"] = Convert.ToBase64String( Encoding.UTF8.GetBytes( Encoding.UTF8.GetString( Convert.FromBase64String( ( string ) signed["payload"] ) ).Replace( "\"rockMajor\": 21", "\"rockMajor\": 20" ) ) ) ), nodeTestKeys ) ) ),
        "InvalidDataException|The package index's signature does not match its contents, so it was not used."),
    ("A changed signature is refused",
        Read( () => Installable( SampleDataPackageIndex.Parse( Changed( nodeSignedIndex, signed =>
        {
            var bytes = Convert.FromBase64String( ( string ) signed["signature"] );
            bytes[10] ^= 1;
            signed["signature"] = Convert.ToBase64String( bytes );
        } ), nodeTestKeys ) ) ),
        "InvalidDataException|The package index's signature does not match its contents, so it was not used."),
    ("Packages outside the signature are ignored",
        Read( () => Entry( SampleDataPackageIndex.Parse( Changed( nodeSignedIndex, signed => signed.Parent["packages"] = new JsonArray() ), nodeTestKeys ) ) ),
        "core-1|21|0.1.0-alpha.14|daec011e5abc"),
    ("Opens the configuration of the published v0.1.0-alpha.10 packages",
        Published( "v0.1.0-alpha.10" ),
        "campuses 7|communications none|church app stub, push off"),
    ("A file this loader cannot read gives a plain message",
        PlainError( "v0.1.0-alpha.10" ),
        "InvalidDataException|This package can't be read by this loader: configuration/church-app.json is not in the form this loader reads. Use the package for this loader's release.|JsonException")
};

var failures = 0;
foreach ( var (name, actual, expected) in checks )
{
    var isMatch = actual == expected;
    failures += isMatch ? 0 : 1;
    Console.WriteLine( isMatch ? $"pass  {name}" : $"FAIL  {name}\n      expected {expected}\n      actual   {actual}" );
}

#if NETFRAMEWORK
var target = ".NET Framework";
#else
var target = ".NET 10";
#endif
Console.WriteLine( failures == 0 ? $"All {checks.Length} checks pass on {target}." : $"{failures} of {checks.Length} checks fail on {target}." );
return failures == 0 ? 0 : 1;

static string Published( string tag )
{
    try
    {
        var source = SampleDataSource.FromFolder( PublishedFolder( tag ) );
        var app = source.ChurchApp == null ? "no church app" : source.ChurchApp.IsComplete ? "church app, push on" : "church app stub, push off";
        return $"campuses {source.Campuses.Campuses.Count}|communications {( source.Communications == null ? "none" : "yes" )}|{app}";
    }
    catch ( Exception ex )
    {
        return $"{ex.Message} {ex.InnerException?.Message}";
    }
}

static string PlainError( string tag )
{
    try
    {
        SampleDataSource.FromFolder( PublishedFolder( tag ) ).Read<int>( "configuration/church-app.json" );
        return "no error";
    }
    catch ( Exception ex )
    {
        return $"{ex.GetType().Name}|{ex.Message}|{ex.InnerException?.GetType().Name}";
    }
}

static string PublishedFolder( string tag ) => Path.Combine( SampleDataSource.FindRoot( AppContext.BaseDirectory ), "loader-compat-check", "published", tag );

static string SigningFolder() => Path.Combine( SampleDataSource.FindRoot( AppContext.BaseDirectory ), "loader-compat-check", "signing" );

// Signs an index with this run's key, as the release workflow signs one with Spark's, and reads it.
SampleDataPackageIndex Signed( string indexJson )
{
    var payload = Encoding.UTF8.GetBytes( indexJson );
    using ( var sha256 = SHA256.Create() )
    {
        var signature = runSigner.SignHash( sha256.ComputeHash( payload ) );
        var file = new JsonObject
        {
            ["signed"] = new JsonObject
            {
                ["keyId"] = "run",
                ["algorithm"] = "ES256",
                ["payload"] = Convert.ToBase64String( payload ),
                ["signature"] = Convert.ToBase64String( signature )
            }
        };

        return SampleDataPackageIndex.Parse( file.ToJsonString(), runKeys );
    }
}

// A signed index file with its "signed" object changed.
static string Changed( string signedIndex, Action<JsonObject> change )
{
    var file = JsonNode.Parse( signedIndex ).AsObject();
    change( file["signed"].AsObject() );
    return file.ToJsonString();
}

// The read's result, or the exception's type and message when it is refused.
static string Read( Func<string> read )
{
    try
    {
        return read();
    }
    catch ( Exception ex )
    {
        return $"{ex.GetType().Name}|{ex.Message}";
    }
}

static string Hex( byte[] bytes ) => BitConverter.ToString( bytes ).Replace( "-", string.Empty ).ToLowerInvariant();

static byte[] Repeat( byte value, int count ) => Enumerable.Repeat( value, count ).ToArray();

static byte[] Range( int start, int count ) => Enumerable.Range( start, count ).Select( i => ( byte ) i ).ToArray();

static string Index( string cardFields ) =>
    "{ \"formatVersion\": 1, \"futureRootField\": true, \"packages\": [ { \"dataset\": \"core-1\", \"name\": \"Core 1\", \"people\": 185, \"campuses\": 1, " + cardFields +
    " \"version\": \"1.0.0\", \"url\": \"v1.0.0/rock-21.0.2/core-1.zip\", \"sha256\": \"" + new string( 'a', 64 ) + "\", \"size\": 1, \"rockVersion\": \"21.0.2\", \"rockMigration\": \"202610141827486_CodeGenerated_20261014\" } ] }";

static string Installable( SampleDataPackageIndex index )
{
    var entry = index.Packages.Single();
    return $"{entry.RockMajor}|{entry.MinimumLoaderVersion ?? "none"}|{entry.IsInstallableOn( "21.0.5" )}|{entry.IsInstallableOn( "20.0.10" )}";
}

static string Card( SampleDataPackageIndex index )
{
    var entry = index.Packages.Single();
    return $"{entry.Dataset}|{entry.BestFor}|{entry.Highlights.Count}|{entry.People}|{entry.Households}|{entry.Campuses}|{entry.TypicalInstallSeconds}";
}

static string Entry( SampleDataPackageIndex index )
{
    var entry = index.Packages.Single();
    return $"{entry.Dataset}|{entry.RockMajor}|{entry.MinimumLoaderVersion}|{entry.Sha256.Substring( 0, 12 )}";
}
