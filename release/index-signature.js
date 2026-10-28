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
    The package index's signature, shared by release/sign-index.js and release/merge-index.js
    (decision 172). The loader's loader/packages/IndexSignature.cs reads the same format:

      {
        "formatVersion": 1,
        "packages": [ ... ],          the same entries, for loaders before 0.1.0-alpha.14
        "signed": {
          "keyId": "sampledata-index-1",
          "algorithm": "ES256",
          "payload": "...",           base64 of the exact UTF-8 bytes that were signed
          "signature": "..."          base64 of the 64-byte ECDSA P-256 signature, r then s
        }
      }

    The payload is the index itself, { formatVersion, signedAt, packages }. A reader checks the signature
    and then reads only the payload, so no canonical form of the JSON is needed.
*/

const crypto = require( "crypto" );
const fs = require( "fs" );
const path = require( "path" );

const ALGORITHM = "ES256";
const KEYS_FILE = path.join( __dirname, "index-signing-keys.json" );

/**
 * Turns a P-256 point, x and y in base64, into a public key.
 */
function publicKey( x, y ) {
    const toUrl = value => Buffer.from( value, "base64" ).toString( "base64url" );
    if ( Buffer.from( x, "base64" ).length !== 32 || Buffer.from( y, "base64" ).length !== 32 ) {
        throw new Error( "A P-256 public key needs a 32-byte x and y." );
    }

    return crypto.createPublicKey( { key: { kty: "EC", crv: "P-256", x: toUrl( x ), y: toUrl( y ) }, format: "jwk" } );
}

/**
 * Reads a keys file in the form of release/index-signing-keys.json: each key with its public key object.
 */
function readKeys( file = KEYS_FILE ) {
    const keys = JSON.parse( fs.readFileSync( file, "utf8" ) ).keys || [];
    const ids = new Set( keys.map( k => k.keyId ) );
    if ( keys.length === 0 || ids.size !== keys.length ) {
        throw new Error( `${file} must list at least one key, each with its own keyId.` );
    }

    return keys.map( k => {
        if ( k.algorithm !== ALGORITHM ) {
            throw new Error( `${file}: key ${k.keyId} uses ${k.algorithm}; only ${ALGORITHM} is read.` );
        }

        return { ...k, publicKey: publicKey( k.x, k.y ) };
    } );
}

/**
 * The exact bytes to sign for an index: { formatVersion, signedAt, packages } as indented JSON.
 */
function payloadBytes( index, signedAt ) {
    const payload = { formatVersion: index.formatVersion, signedAt, packages: index.packages };
    return Buffer.from( JSON.stringify( payload, null, 2 ) + "\n", "utf8" );
}

/**
 * The signed index file's text, from the signed bytes and their signature.
 */
function signedFile( payload, keyId, signature ) {
    const index = JSON.parse( payload.toString( "utf8" ) );
    const file = {
        formatVersion: index.formatVersion,
        packages: index.packages,
        signed: { keyId, algorithm: ALGORITHM, payload: payload.toString( "base64" ), signature: signature.toString( "base64" ) }
    };

    return JSON.stringify( file, null, 2 ) + "\n";
}

/**
 * Checks a signed index file's signature with the given keys and returns the signed index, read only
 * from the signed bytes. Throws when it is not signed, the key is unknown, or the signature does not match.
 */
function verify( text, keys ) {
    const signed = JSON.parse( text ).signed;
    if ( !signed ) {
        throw new Error( "The package index is not signed." );
    }

    if ( signed.algorithm !== ALGORITHM ) {
        throw new Error( `The package index is signed with ${signed.algorithm}; only ${ALGORITHM} is read.` );
    }

    const key = keys.find( k => k.keyId === signed.keyId );
    if ( !key ) {
        throw new Error( `The package index is signed by key '${signed.keyId}', which is not in the keys file.` );
    }

    const payload = Buffer.from( signed.payload || "", "base64" );
    const signature = Buffer.from( signed.signature || "", "base64" );
    const isMatch = signature.length === 64
        && crypto.verify( "sha256", payload, { key: key.publicKey, dsaEncoding: "ieee-p1363" }, signature );
    if ( !isMatch ) {
        throw new Error( "The package index's signature does not match its contents." );
    }

    return JSON.parse( payload.toString( "utf8" ) );
}

module.exports = { ALGORITHM, KEYS_FILE, publicKey, readKeys, payloadBytes, signedFile, verify };
