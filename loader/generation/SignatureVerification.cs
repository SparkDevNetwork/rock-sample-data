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
using System.Security.Cryptography;
using System.Text;

namespace RockSampleData.Loader.Generation
{
    /*
        10/29/26 - CLAUDE

        A port of SignatureDocumentService.CalculateSignatureVerificationHash (Rock 20 and 21 match). Rock's comment
        says xxHash, but the code is SHA-1 over seven fields joined by "|" and a line break, Base64 with /, +, and =
        turned into x, and an "A" in front. The line break is the one inside Rock's verbatim string. Rock's Windows
        build has CRLF there, which is what Rock 21's own Rock.dll gives; loader-compat-check holds a vector computed
        by calling that Rock.dll. The signed time is cut to the millisecond and written with "o", as Rock does.

        HtmlEncode ports HttpUtility.HtmlEncode on .NET Framework, which Rock's registration entry runs on the signed
        name, email, IP address, and user agent before it stores and hashes them.

        Reason: A signed document's hash matches what Rock would compute, so it reads as Rock wrote it.
    */

    /// <summary>
    /// Rock's signature verification hash and the HTML encoding its registration entry applies to signature fields.
    /// </summary>
    internal static class SignatureVerification
    {
        /// <summary>
        /// The line break inside Rock's verbatim concatenation string.
        /// </summary>
        private const string RockLineBreak = "\r\n";

        /// <summary>
        /// Rock's SignatureDocumentService.CalculateSignatureVerificationHash.
        /// </summary>
        public static string Hash( string signedDocumentText, string signedClientIp, string signedClientUserAgent, DateTime? signedDateTime, int? signedByPersonAliasId, string signatureData, string signedName )
        {
            var signed = string.Empty;
            if ( signedDateTime.HasValue )
            {
                var value = signedDateTime.Value;
                signed = new DateTime( value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Millisecond, DateTimeKind.Unspecified ).ToString( "o", CultureInfo.InvariantCulture );
            }

            var separator = "|" + RockLineBreak;
            var text = string.Join( separator, signedDocumentText, signedClientIp, signedClientUserAgent, signed,
                signedByPersonAliasId?.ToString( CultureInfo.InvariantCulture ), signatureData, signedName );
            using ( var sha1 = SHA1.Create() )
            {
                var base64 = Convert.ToBase64String( sha1.ComputeHash( Encoding.UTF8.GetBytes( text ) ) );
                return "A" + base64.Replace( '/', 'x' ).Replace( '+', 'x' ).Replace( '=', 'x' );
            }
        }

        /// <summary>
        /// HttpUtility.HtmlEncode as .NET Framework runs it: the five markup characters, and characters 160 to 255 as
        /// numeric references. Other characters pass through.
        /// </summary>
        public static string HtmlEncode( string value )
        {
            if ( string.IsNullOrEmpty( value ) )
            {
                return value;
            }

            var builder = new StringBuilder( value.Length );
            for ( var i = 0; i < value.Length; i++ )
            {
                var c = value[i];
                switch ( c )
                {
                    case '<':
                        builder.Append( "&lt;" );
                        break;

                    case '>':
                        builder.Append( "&gt;" );
                        break;

                    case '"':
                        builder.Append( "&quot;" );
                        break;

                    case '\'':
                        builder.Append( "&#39;" );
                        break;

                    case '&':
                        builder.Append( "&amp;" );
                        break;

                    default:
                        if ( c >= 160 && c < 256 )
                        {
                            builder.Append( "&#" ).Append( ( ( int ) c ).ToString( CultureInfo.InvariantCulture ) ).Append( ';' );
                        }
                        else
                        {
                            builder.Append( c );
                        }

                        break;
                }
            }

            return builder.ToString();
        }
    }
}
