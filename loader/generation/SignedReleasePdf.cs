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
using System.Text;
using System.Text.RegularExpressions;

namespace RockSampleData.Loader.Generation
{
    /*
        10/29/26 - CLAUDE

        Rock prints a signed release to PDF from the signed text plus a signature box (ElectronicSignatureHelper
        GetSignatureInformationHtml): the signer's name, the signed date, the IP address, the agreement sentence, and
        the reference code, which is the verification hash. Rock's PDF engine is a headless browser the loader cannot
        run, so this small writer draws the same content as text in the PDF standard fonts, which every reader has. It
        needs no package, so it builds the same on .NET 10 and .NET Framework, and the same inputs always give the same
        bytes.

        Reason: Every signed release opens as a real PDF with the content Rock's would show.
    */

    /// <summary>
    /// Draws a signed release as a small PDF: its title and paragraphs, then the signature box.
    /// </summary>
    internal static class SignedReleasePdf
    {
        private const double PageWidth = 612;
        private const double PageHeight = 792;
        private const double Margin = 72;
        private const double TitleSize = 16;
        private const double BodySize = 11;
        private const double BodyLeading = 15;
        private const double CodeSize = 9;

        /// <summary>
        /// The sentence Rock's signature box shows under the signature.
        /// </summary>
        public const string Agreement = "I agree to the statements above and understand this is a legal representation of my signature.";

        /// <summary>
        /// Helvetica's character widths, in thousandths of the font size, for the printable ASCII characters.
        /// </summary>
        private static readonly int[] HelveticaWidths =
        {
            278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
            556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
            1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
            667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
            333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
            556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
        };

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
            var page = new PageWriter();
            foreach ( Match block in Blocks.Matches( signedHtml ) )
            {
                var text = WebUtility.HtmlDecode( Tags.Replace( block.Groups[2].Value, string.Empty ) ).Trim();
                if ( block.Groups[1].Value == "h2" )
                {
                    page.Paragraph( text, "F2", TitleSize, TitleSize + 6, 10 );
                }
                else
                {
                    page.Paragraph( text, "F1", BodySize, BodyLeading, 8 );
                }
            }

            page.SignatureBox( signerName, signed.ToString( "M/d/yyyy", CultureInfo.InvariantCulture ), ipAddress, hash );
            return Assemble( title, page.Pages() );
        }

        /// <summary>
        /// Writes the PDF file: catalog, page tree, the three standard fonts, and each page with its content stream.
        /// </summary>
        private static byte[] Assemble( string title, List<string> pages )
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                null,
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>",
                $"<< /Title ({Escape( title )}) >>"
            };

            var kids = new List<string>();
            foreach ( var content in pages )
            {
                var contentNumber = objects.Count + 2;
                kids.Add( $"{objects.Count + 1} 0 R" );
                objects.Add( $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Number( PageWidth )} {Number( PageHeight )}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> >> /Contents {contentNumber} 0 R >>" );
                objects.Add( $"<< /Length {Latin1.GetByteCount( content )} >>\nstream\n{content}\nendstream" );
            }

            objects[1] = $"<< /Type /Pages /Kids [{string.Join( " ", kids )}] /Count {kids.Count} >>";

            var pdf = new StringBuilder( "%PDF-1.4\n" );
            var offsets = new List<int>();
            for ( var i = 0; i < objects.Count; i++ )
            {
                offsets.Add( Latin1.GetByteCount( pdf.ToString() ) );
                pdf.Append( i + 1 ).Append( " 0 obj\n" ).Append( objects[i] ).Append( "\nendobj\n" );
            }

            var xref = Latin1.GetByteCount( pdf.ToString() );
            pdf.Append( "xref\n0 " ).Append( objects.Count + 1 ).Append( "\n0000000000 65535 f \n" );
            foreach ( var offset in offsets )
            {
                pdf.Append( offset.ToString( "D10", CultureInfo.InvariantCulture ) ).Append( " 00000 n \n" );
            }

            pdf.Append( "trailer\n<< /Size " ).Append( objects.Count + 1 ).Append( " /Root 1 0 R /Info 6 0 R >>\nstartxref\n" ).Append( xref ).Append( "\n%%EOF\n" );
            return Latin1.GetBytes( pdf.ToString() );
        }

        /// <summary>
        /// The PDF bytes are Latin-1: text is already in WinAnsi codes, one byte each.
        /// </summary>
        private static readonly Encoding Latin1 = Encoding.GetEncoding( "ISO-8859-1" );

        /// <summary>
        /// A PDF string literal in WinAnsi: parentheses and backslashes escaped, typographic quotes and dashes mapped
        /// to their WinAnsi codes, and anything WinAnsi cannot show as a question mark.
        /// </summary>
        private static string Escape( string text )
        {
            var builder = new StringBuilder( text.Length );
            foreach ( var c in text )
            {
                var code = WinAnsi( c );
                if ( code == '(' || code == ')' || code == '\\' )
                {
                    builder.Append( '\\' ).Append( code );
                }
                else
                {
                    builder.Append( code );
                }
            }

            return builder.ToString();
        }

        private static char WinAnsi( char c )
        {
            switch ( c )
            {
                case '‘': return '\u0091';
                case '’': return '\u0092';
                case '“': return '\u0093';
                case '”': return '\u0094';
                case '–': return '\u0096';
                case '—': return '\u0097';
            }

            return ( c >= 32 && c <= 126 ) || ( c >= 160 && c <= 255 ) ? c : '?';
        }

        private static double Width( string text, string font, double size )
        {
            if ( font == "F3" )
            {
                return text.Length * 600 * size / 1000;
            }

            var total = text.Sum( c => c >= 32 && c <= 126 ? HelveticaWidths[c - 32] : 556 );

            // Helvetica-Bold runs a little wider than Helvetica.
            return total * size / 1000 * ( font == "F2" ? 1.08 : 1.0 );
        }

        private static string Number( double value ) => value.ToString( "0.##", CultureInfo.InvariantCulture );

        /// <summary>
        /// Lays text out top to bottom, starting a new page when one fills.
        /// </summary>
        private sealed class PageWriter
        {
            private readonly List<StringBuilder> _pages = new List<StringBuilder> { new StringBuilder() };
            private double _y = PageHeight - Margin;

            private StringBuilder Current => _pages[_pages.Count - 1];

            public List<string> Pages() => _pages.Select( p => p.ToString().TrimEnd( '\n' ) ).ToList();

            /// <summary>
            /// Writes a paragraph, wrapped to the page width, then leaves a gap after it.
            /// </summary>
            public void Paragraph( string text, string font, double size, double leading, double gap )
            {
                foreach ( var line in Wrap( text, font, size, PageWidth - ( 2 * Margin ) ) )
                {
                    Room( leading );
                    _y -= leading;
                    Text( Margin, _y, font, size, line );
                }

                _y -= gap;
            }

            /// <summary>
            /// Rock's signature box: a gray panel with the typed name, the details, the agreement, and the reference code.
            /// </summary>
            public void SignatureBox( string name, string signedDate, string ipAddress, string hash )
            {
                var width = PageWidth - ( 2 * Margin );
                var agreement = Wrap( Agreement, "F1", 10, width - 20 );
                var height = 20 + 22 + ( 3 * 14 ) + 8 + ( agreement.Count * 13 ) + 8 + 12;
                Room( height + 12 );
                _y -= 12;
                var top = _y;
                var bottom = top - height;
                Current.Append( "q 0.96 g 0 G 1 w " ).Append( Number( Margin ) ).Append( ' ' ).Append( Number( bottom ) ).Append( ' ' )
                    .Append( Number( width ) ).Append( ' ' ).Append( Number( height ) ).Append( " re B Q\n" );

                var y = top - 30;
                Text( Margin + 10, y, "F2", 14, name );
                y -= 22;
                foreach ( var (label, value) in new[] { ("Name:", name), ("Signed:", signedDate), ("IP:", ipAddress) } )
                {
                    Text( Margin + 10, y, "F2", 10, label );
                    Text( Margin + 10 + Width( label + " ", "F2", 10 ), y, "F1", 10, value );
                    y -= 14;
                }

                y -= 8;
                foreach ( var line in agreement )
                {
                    Text( Margin + 10, y, "F1", 10, line );
                    y -= 13;
                }

                var code = $"Reference Code: {hash}";
                Text( Margin + width - 10 - Width( code, "F3", CodeSize ), bottom + 10, "F3", CodeSize, code );
                _y = bottom;
            }

            private void Room( double needed )
            {
                if ( _y - needed < Margin )
                {
                    _pages.Add( new StringBuilder() );
                    _y = PageHeight - Margin;
                }
            }

            private void Text( double x, double y, string font, double size, string text )
            {
                Current.Append( "BT /" ).Append( font ).Append( ' ' ).Append( Number( size ) ).Append( " Tf " ).Append( Number( x ) ).Append( ' ' ).Append( Number( y ) )
                    .Append( " Td (" ).Append( Escape( text ) ).Append( ") Tj ET\n" );
            }

            private static List<string> Wrap( string text, string font, double size, double width )
            {
                var lines = new List<string>();
                var line = string.Empty;
                foreach ( var word in text.Split( new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries ) )
                {
                    var next = line.Length == 0 ? word : line + " " + word;
                    if ( line.Length > 0 && Width( next, font, size ) > width )
                    {
                        lines.Add( line );
                        line = word;
                    }
                    else
                    {
                        line = next;
                    }
                }

                if ( line.Length > 0 )
                {
                    lines.Add( line );
                }

                return lines;
            }
        }
    }
}
