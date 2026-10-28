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

/*
    Signs the package index (decision 172). The release workflow runs it on the index that
    release/merge-index.js wrote, before anything is published. The format is in release/index-signature.js
    and the loader README, under "The package index".

    Usage:
      node release/sign-index.js --in <index.json> --out <signed index.json> --vault-name <vault> --key <name/version>
      node release/sign-index.js --in <index.json> --out <signed index.json> --test-key <private key .pem>
      node release/sign-index.js --new-test-key <private key .pem>
      node release/sign-index.js --self-test

    --vault-name, --key  Sign in Azure Key Vault with "az keyvault key sign". The signed-in identity needs
                         the Sample Data Index Signer role on the key. --key is the key's name and version,
                         as the "vaultKey" of a key in release/index-signing-keys.json.
    --test-key           Sign with a local P-256 key instead, for testing without the vault. The loader
                         never trusts it: the signed index names the key "test".
    --new-test-key       Writes a new local P-256 private key for --test-key.
    --self-test          Signs and checks a sample index with a throwaway key, and checks that a changed
                         payload, a changed signature, an unknown key, and an unsigned index are refused.

    The script checks its own signature before it writes the file, with the keys file for a vault key, so a
    signature the loader would refuse is never published.
*/

const crypto = require( "crypto" );
const fs = require( "fs" );
const path = require( "path" );
const { spawnSync } = require( "child_process" );

const signature = require( "./index-signature" );

const TEST_KEY_ID = "test";

// #region Functions

function readArguments( args ) {
    const options = {};
    for ( let i = 0; i < args.length; i++ ) {
        if ( !args[ i ].startsWith( "--" ) ) {
            continue;
        }

        const name = args[ i ].substring( 2 );
        const next = args[ i + 1 ];
        if ( next === undefined || next.startsWith( "--" ) ) {
            options[ name ] = true;
        }
        else {
            options[ name ] = next;
            i++;
        }
    }

    return options;
}

/**
 * Asks Azure Key Vault to sign a SHA-256 digest. Key Vault returns the signature as r then s.
 */
function signInVault( vaultName, vaultKey, digest ) {
    const [ name, version ] = vaultKey.split( "/" );
    if ( !name || !version ) {
        throw new Error( `--key must be a key name and version, such as sampledata-index-1/<version>, not '${vaultKey}'.` );
    }

    const result = spawnSync( "az", [
        "keyvault", "key", "sign",
        "--vault-name", vaultName, "--name", name, "--version", version,
        "--algorithm", signature.ALGORITHM, "--digest", digest.toString( "base64" ),
        "--only-show-errors", "-o", "json"
    ], { encoding: "utf8", shell: process.platform === "win32" } );

    if ( result.status !== 0 ) {
        throw new Error( `Key Vault did not sign the index: ${( result.stderr || result.error || "" ).toString().trim()}` );
    }

    // Key Vault gives base64url; Node's base64 decoder reads both alphabets.
    return Buffer.from( JSON.parse( result.stdout ).result, "base64" );
}

/**
 * Signs a digest with a local private key, as r then s.
 */
function signWithKey( privateKey, payload ) {
    return crypto.sign( "sha256", payload, { key: privateKey, dsaEncoding: "ieee-p1363" } );
}

/**
 * The test key's public half, in the keys file's form.
 */
function testKeys( privateKey ) {
    const jwk = crypto.createPublicKey( privateKey ).export( { format: "jwk" } );
    const toBase64 = value => Buffer.from( value, "base64url" ).toString( "base64" );
    return [ { keyId: TEST_KEY_ID, algorithm: signature.ALGORITHM, x: toBase64( jwk.x ), y: toBase64( jwk.y ), publicKey: crypto.createPublicKey( privateKey ) } ];
}

/**
 * Reads an unsigned index written by release/merge-index.js.
 */
function readUnsigned( file ) {
    const index = JSON.parse( fs.readFileSync( file, "utf8" ) );
    if ( index.signed ) {
        throw new Error( `${file} is already signed.` );
    }

    if ( index.formatVersion !== 1 || !Array.isArray( index.packages ) ) {
        throw new Error( `${file} is not a format 1 package index.` );
    }

    return index;
}

/**
 * Signs an index and checks the signature. Returns the signed file's text.
 */
function sign( index, signer, keyId, keys ) {
    const payload = signature.payloadBytes( index, new Date().toISOString() );
    const digest = crypto.createHash( "sha256" ).update( payload ).digest();
    const text = signature.signedFile( payload, keyId, signer( payload, digest ) );

    // Never publish a signature the loader would refuse.
    signature.verify( text, keys );
    return text;
}

function expectRefused( name, action ) {
    try {
        action();
    }
    catch {
        console.log( `pass  ${name} is refused` );
        return 0;
    }

    console.log( `FAIL  ${name} was accepted` );
    return 1;
}

function selfTest() {
    const { privateKey } = crypto.generateKeyPairSync( "ec", { namedCurve: "P-256" } );
    const keys = testKeys( privateKey );
    const index = { formatVersion: 1, packages: [ { dataset: "core-1", sha256: "a".repeat( 64 ) } ] };
    const text = sign( index, payload => signWithKey( privateKey, payload ), TEST_KEY_ID, keys );
    const read = signature.verify( text, keys );
    let failures = read.packages[ 0 ].dataset === "core-1" && read.signedAt ? 0 : 1;
    console.log( failures === 0 ? "pass  a signed index is read from its payload" : "FAIL  the signed index did not read back" );

    const file = JSON.parse( text );
    const changed = mutate => {
        const copy = JSON.parse( JSON.stringify( file ) );
        mutate( copy );
        return JSON.stringify( copy );
    };

    failures += expectRefused( "a changed payload", () => signature.verify( changed( f => {
        f.signed.payload = Buffer.from( Buffer.from( f.signed.payload, "base64" ).toString( "utf8" ).replace( "core-1", "core-3" ) ).toString( "base64" );
    } ), keys ) );
    failures += expectRefused( "a changed signature", () => signature.verify( changed( f => {
        const bytes = Buffer.from( f.signed.signature, "base64" );
        bytes[ 10 ] ^= 1;
        f.signed.signature = bytes.toString( "base64" );
    } ), keys ) );
    failures += expectRefused( "an unknown key", () => signature.verify( changed( f => { f.signed.keyId = "other"; } ), keys ) );
    failures += expectRefused( "an unsigned index", () => signature.verify( changed( f => { delete f.signed; } ), keys ) );
    failures += expectRefused( "Spark's keys on a test signature", () => signature.verify( text, signature.readKeys() ) );

    const sparkKeys = signature.readKeys();
    const isKeysFileGood = sparkKeys.filter( k => k.status === "active" ).length === 1 && sparkKeys.some( k => k.status === "spare" );
    failures += isKeysFileGood ? 0 : 1;
    console.log( isKeysFileGood ? "pass  the keys file has one active key and a spare" : "FAIL  the keys file needs one active key and a spare" );

    console.log( failures === 0 ? "All signing checks pass." : `${failures} signing checks fail.` );
    return failures === 0 ? 0 : 1;
}

// #endregion Functions

function main() {
    const options = readArguments( process.argv.slice( 2 ) );

    if ( options[ "self-test" ] ) {
        process.exitCode = selfTest();
        return;
    }

    if ( options[ "new-test-key" ] ) {
        const { privateKey } = crypto.generateKeyPairSync( "ec", { namedCurve: "P-256" } );
        fs.writeFileSync( options[ "new-test-key" ], privateKey.export( { type: "pkcs8", format: "pem" } ) );
        console.log( `Wrote a test signing key to ${options[ "new-test-key" ]}. The loader never trusts it.` );
        return;
    }

    for ( const required of [ "in", "out" ] ) {
        if ( !options[ required ] ) {
            throw new Error( `--${required} is required.` );
        }
    }

    const index = readUnsigned( options.in );
    let text;

    if ( options[ "test-key" ] ) {
        const privateKey = crypto.createPrivateKey( fs.readFileSync( options[ "test-key" ], "utf8" ) );
        text = sign( index, payload => signWithKey( privateKey, payload ), TEST_KEY_ID, testKeys( privateKey ) );
    }
    else {
        if ( !options[ "vault-name" ] || !options.key ) {
            throw new Error( "Give --vault-name and --key to sign in Key Vault, or --test-key to sign with a local key." );
        }

        const keys = signature.readKeys();
        const key = keys.find( k => k.vaultKey === options.key );
        if ( !key ) {
            throw new Error( `${options.key} is not a vaultKey in release/index-signing-keys.json, so the loader would not trust its signature.` );
        }

        text = sign( index, ( _payload, digest ) => signInVault( options[ "vault-name" ], options.key, digest ), key.keyId, keys );
    }

    fs.mkdirSync( path.dirname( path.resolve( options.out ) ), { recursive: true } );
    fs.writeFileSync( options.out, text );
    console.log( `Signed ${options.out} with key ${JSON.parse( text ).signed.keyId}. The signature checks out.` );
}

try {
    main();
}
catch ( error ) {
    console.error( error.message );
    process.exitCode = 1;
}
