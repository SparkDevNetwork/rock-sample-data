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
using System.Text;

namespace RockSampleData.Loader.Generation
{
    /*
        11/19/26 - CLAUDE

        Rock prints its documents to PDF with a headless browser the loader cannot run. The signed releases, the
        background check reports, and the benevolence documents are drawn instead as text in the PDF standard fonts,
        which every reader has. One writer lays the text out on letter pages and assembles the file. It needs no
        package, so it builds the same on .NET 10 and .NET Framework, and the same inputs always give the same bytes.

        Reason: Every drawn document opens as a real PDF, and the three kinds share one layout engine.
    */

    /// <summary>
    /// The three PDF standard fonts the drawn documents use.
    /// </summary>
    internal enum PdfFont
    {
        Regular,
        Bold,
        Mono
    }

    /// <summary>
    /// Lays text out top to bottom on letter pages, starting a new page when one fills, and writes the PDF file.
    /// </summary>
    internal sealed class PdfPages
    {
        public const double PageWidth = 612;
        public const double PageHeight = 792;
        public const double Margin = 72;

        /// <summary>
        /// The width text can use between the margins.
        /// </summary>
        public const double TextWidth = PageWidth - ( 2 * Margin );

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

        /// <summary>
        /// The PDF bytes are Latin-1: text is already in WinAnsi codes, one byte each.
        /// </summary>
        private static readonly Encoding Latin1 = Encoding.GetEncoding( "ISO-8859-1" );

        private readonly List<StringBuilder> _pages = new List<StringBuilder> { new StringBuilder() };

        /// <summary>
        /// Where the next line goes, in points from the bottom of the page.
        /// </summary>
        public double Y { get; set; } = PageHeight - Margin;

        private StringBuilder Current => _pages[_pages.Count - 1];

        /// <summary>
        /// Writes a paragraph, wrapped to the text width, then leaves a gap after it.
        /// </summary>
        public void Paragraph( string text, PdfFont font, double size, double leading, double gap )
        {
            foreach ( var line in Wrap( text, font, size, TextWidth ) )
            {
                Room( leading );
                Y -= leading;
                Text( Margin, Y, font, size, line );
            }

            Y -= gap;
        }

        /// <summary>
        /// Writes a label in bold and its value after it on one line, then moves down by the leading.
        /// </summary>
        public void LabelValue( string label, string value, double size, double leading, double x = Margin )
        {
            Room( leading );
            Y -= leading;
            Text( x, Y, PdfFont.Bold, size, label );
            Text( x + Width( label + " ", PdfFont.Bold, size ), Y, PdfFont.Regular, size, value );
        }

        /// <summary>
        /// Writes cells on one line, each at its own left edge, then moves down by the leading.
        /// </summary>
        public void Row( IReadOnlyList<(double X, string Text)> cells, PdfFont font, double size, double leading )
        {
            Room( leading );
            Y -= leading;
            foreach ( var (x, text) in cells )
            {
                Text( x, Y, font, size, text );
            }
        }

        /// <summary>
        /// Draws a thin line across the text width at the current position, then leaves a gap below it.
        /// </summary>
        public void Rule( double gap )
        {
            Room( gap );
            Current.Append( "q 0.6 G 0.5 w " ).Append( Number( Margin ) ).Append( ' ' ).Append( Number( Y ) ).Append( " m " )
                .Append( Number( PageWidth - Margin ) ).Append( ' ' ).Append( Number( Y ) ).Append( " l S Q\n" );
            Y -= gap;
        }

        /// <summary>
        /// Draws a light gray panel with a thin border.
        /// </summary>
        public void Panel( double x, double bottom, double width, double height )
        {
            Current.Append( "q 0.96 g 0 G 1 w " ).Append( Number( x ) ).Append( ' ' ).Append( Number( bottom ) ).Append( ' ' )
                .Append( Number( width ) ).Append( ' ' ).Append( Number( height ) ).Append( " re B Q\n" );
        }

        /// <summary>
        /// Starts a new page when less than the needed height is left above the bottom margin.
        /// </summary>
        public void Room( double needed )
        {
            if ( Y - needed < Margin )
            {
                _pages.Add( new StringBuilder() );
                Y = PageHeight - Margin;
            }
        }

        /// <summary>
        /// Writes one line of text with its baseline at the given point.
        /// </summary>
        public void Text( double x, double y, PdfFont font, double size, string text )
        {
            Current.Append( "BT /" ).Append( FontName( font ) ).Append( ' ' ).Append( Number( size ) ).Append( " Tf " ).Append( Number( x ) ).Append( ' ' ).Append( Number( y ) )
                .Append( " Td (" ).Append( Escape( text ) ).Append( ") Tj ET\n" );
        }

        /// <summary>
        /// Writes the PDF file: catalog, page tree, the three standard fonts, the title, and each page with its content stream.
        /// </summary>
        public byte[] Save( string title )
        {
            var pages = _pages.Select( p => p.ToString().TrimEnd( '\n' ) ).ToList();
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
        /// How wide a line of text is, in points. Helvetica-Bold runs a little wider than Helvetica.
        /// </summary>
        public static double Width( string text, PdfFont font, double size )
        {
            if ( font == PdfFont.Mono )
            {
                return text.Length * 600 * size / 1000;
            }

            var total = text.Sum( c => c >= 32 && c <= 126 ? HelveticaWidths[c - 32] : 556 );
            return total * size / 1000 * ( font == PdfFont.Bold ? 1.08 : 1.0 );
        }

        /// <summary>
        /// Splits text into lines no wider than the width, breaking between words.
        /// </summary>
        public static List<string> Wrap( string text, PdfFont font, double size, double width )
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

        public static string Number( double value ) => value.ToString( "0.##", CultureInfo.InvariantCulture );

        private static string FontName( PdfFont font ) => font switch
        {
            PdfFont.Bold => "F2",
            PdfFont.Mono => "F3",
            _ => "F1"
        };

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
    }
}
