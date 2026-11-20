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
using System.Net;
using System.Text.RegularExpressions;

namespace RockSampleData.Loader.Generation
{
    /*
        10/29/26 - CLAUDE

        Rock prints a signed release to PDF from the signed text plus a signature box (ElectronicSignatureHelper
        GetSignatureInformationHtml): the signer's name, the signed date, the IP address, the agreement sentence, and
        the reference code, which is the verification hash. Rock's PDF engine is a headless browser the loader cannot
        run, so the shared PdfPages writer draws the same content as text in the PDF standard fonts.

        Reason: Every signed release opens as a real PDF with the content Rock's would show.
    */

    /// <summary>
    /// Draws a signed release as a small PDF: its title and paragraphs, then the signature box.
    /// </summary>
    internal static class SignedReleasePdf
    {
        private const double TitleSize = 16;
        private const double BodySize = 11;
        private const double BodyLeading = 15;
        private const double CodeSize = 9;

        /// <summary>
        /// The sentence Rock's signature box shows under the signature.
        /// </summary>
        public const string Agreement = "I agree to the statements above and understand this is a legal representation of my signature.";

        private static readonly Regex Blocks = new Regex( @"<(h2|p)>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.CultureInvariant );
        private static readonly Regex Tags = new Regex( "<[^>]+>", RegexOptions.CultureInvariant );

        /// <summary>
        /// Draws the release.
        /// </summary>
        /// <param name="title">The document's name, for the PDF's properties.</param>
        /// <param name="signedHtml">The signed text: an h2 title and p paragraphs, as the release's Lava renders.</param>
        /// <param name="signerName">The name the signature box shows: the signer's full name.</param>
        /// <param name="signed">When the release was signed.</param>
        /// <param name="ipAddress">The signer's IP address.</param>
        /// <param name="hash">The signature verification hash, shown as the reference code.</param>
        public static byte[] Draw( string title, string signedHtml, string signerName, DateTime signed, string ipAddress, string hash )
        {
            var page = new PdfPages();
            foreach ( Match block in Blocks.Matches( signedHtml ) )
            {
                var text = WebUtility.HtmlDecode( Tags.Replace( block.Groups[2].Value, string.Empty ) ).Trim();
                if ( block.Groups[1].Value == "h2" )
                {
                    page.Paragraph( text, PdfFont.Bold, TitleSize, TitleSize + 6, 10 );
                }
                else
                {
                    page.Paragraph( text, PdfFont.Regular, BodySize, BodyLeading, 8 );
                }
            }

            SignatureBox( page, signerName, signed.ToString( "M/d/yyyy", CultureInfo.InvariantCulture ), ipAddress, hash );
            return page.Save( title );
        }

        /// <summary>
        /// Rock's signature box: a gray panel with the typed name, the details, the agreement, and the reference code.
        /// </summary>
        private static void SignatureBox( PdfPages page, string name, string signedDate, string ipAddress, string hash )
        {
            var left = PdfPages.Margin;
            var width = PdfPages.TextWidth;
            var agreement = PdfPages.Wrap( Agreement, PdfFont.Regular, 10, width - 20 );
            var height = 20 + 22 + ( 3 * 14 ) + 8 + ( agreement.Count * 13 ) + 8 + 12;
            page.Room( height + 12 );
            page.Y -= 12;
            var top = page.Y;
            var bottom = top - height;
            page.Panel( left, bottom, width, height );

            var y = top - 30;
            page.Text( left + 10, y, PdfFont.Bold, 14, name );
            y -= 22;
            foreach ( var (label, value) in new[] { ("Name:", name), ("Signed:", signedDate), ("IP:", ipAddress) } )
            {
                page.Text( left + 10, y, PdfFont.Bold, 10, label );
                page.Text( left + 10 + PdfPages.Width( label + " ", PdfFont.Bold, 10 ), y, PdfFont.Regular, 10, value );
                y -= 14;
            }

            y -= 8;
            foreach ( var line in agreement )
            {
                page.Text( left + 10, y, PdfFont.Regular, 10, line );
                y -= 13;
            }

            var code = $"Reference Code: {hash}";
            page.Text( left + width - 10 - PdfPages.Width( code, PdfFont.Mono, CodeSize ), bottom + 10, PdfFont.Mono, CodeSize, code );
            page.Y = bottom;
        }
    }
}
