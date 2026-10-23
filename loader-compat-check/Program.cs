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
    The last two checks read a package index from before the card fields joined it and one with fields
    this loader does not know, so both builds stay able to read an older and a newer index.
*/

// Rock's Encryption salt and HKDF label, as in loader/database/RockKeys.cs and Rock/Security/Encryption.cs.
var rockSalt = Encoding.ASCII.GetBytes( "rsduYVC2leenXKTLYLkO9qsWU95HGCvWlbXcBTjtrj5dBJ7RPeGYiw7U3lZE+LWkT+jGrLP9deRMc8sUHJtc/wu2l4vANBx5f+p1zpRwQ2bB/E6Ta8k7haPiTRc4wYhrmWMrg8VfQ4MhAsSlijIfT9u+DszEkB2ba2k0FIPMSWk=" );
var rockInfo = Encoding.UTF8.GetBytes( "Rock.Security.Encryption.v2" );
var rockKeyBytes = KeyDerivation.Pbkdf2( Encoding.UTF8.GetBytes( "compat-check-data-encryption-key" ), rockSalt, 1000, HashAlgorithmName.SHA1, 32 );
var keys = RockKeys.FromKeys( "0123456789ABCDEF0123456789abcdef0123456789ABCDEF0123456789ABCDEF", "compat-check-data-encryption-key" );
var random = new SeededRandom( "rock-sample-data:compat-check" );

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
        Card( SampleDataPackageIndex.Parse( Index( "" ) ) ),
        "core-1||0|185|0|1|0"),
    ("Package index with the card fields and fields this loader does not know",
        Card( SampleDataPackageIndex.Parse( Index( "\"bestFor\": \"Tests.\", \"highlights\": [ \"Photos\", \" \", \"Giving\" ], \"households\": 87, \"typicalInstallSeconds\": 10, \"futureField\": { \"nested\": [ 1, 2 ] }," ) ) ),
        "core-1|Tests.|2|185|87|1|10")
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

static string Hex( byte[] bytes ) => BitConverter.ToString( bytes ).Replace( "-", string.Empty ).ToLowerInvariant();

static byte[] Repeat( byte value, int count ) => Enumerable.Repeat( value, count ).ToArray();

static byte[] Range( int start, int count ) => Enumerable.Range( start, count ).Select( i => ( byte ) i ).ToArray();

static string Index( string cardFields ) =>
    "{ \"formatVersion\": 1, \"futureRootField\": true, \"packages\": [ { \"dataset\": \"core-1\", \"name\": \"Core 1\", \"people\": 185, \"campuses\": 1, " + cardFields +
    " \"version\": \"1.0.0\", \"url\": \"v1.0.0/rock-21.0.2/core-1.zip\", \"sha256\": \"" + new string( 'a', 64 ) + "\", \"size\": 1, \"rockVersion\": \"21.0.2\", \"rockMigration\": \"202610141827486_CodeGenerated_20261014\" } ] }";

static string Card( SampleDataPackageIndex index )
{
    var entry = index.Packages.Single();
    return $"{entry.Dataset}|{entry.BestFor}|{entry.Highlights.Count}|{entry.People}|{entry.Households}|{entry.Campuses}|{entry.TypicalInstallSeconds}";
}
