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

using System.Numerics;

using RockSampleData.Loader.Packages;

using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RockSampleData.Loader.Generation
{
    /*
        10/15/26 - CLAUDE

        Draws scanned check images (front and back) at install time. Each bank's blank
        check (personal and business) is drawn once; every check copies it and adds only that
        check's details, so a check costs a few milliseconds. Uses ImageSharp, which Rock
        already ships and which runs on .NET Framework and modern .NET alike.

        Everything printed comes from the database rows the loader writes for the same gift:
        the writer's name and address, the check number (TransactionCode), the account's last
        four digits (AccountNumberMasked), the amount, and the date. The bank line uses the
        E-13B shapes drawn here, so no MICR font is needed.

        Fonts come from the repository, never from the machine: handwriting and signature
        fonts and the printed Open Sans (the same files Rock ships) all live in content/fonts.

        Reason: Scanned checks are part of the giving story, and each image must match its gift.
    */

    /// <summary>
    /// What one check shows.
    /// </summary>
    internal sealed record CheckImageData(
        IReadOnlyList<string> WriterLines,
        bool IsBusiness,
        int CheckNumber,
        DateTime WrittenDate,
        string Payee,
        decimal Amount,
        string Memo,
        string Signature,
        string Bank,
        string RoutingNumber,
        string AccountNumber,
        int HandwritingIndex,
        DateTime DepositDate,
        string DepositBank,
        string DepositAccountLast4,
        long SequenceNumber );

    /// <summary>
    /// Draws color JPEG check images from templates.
    /// </summary>
    internal sealed class CheckImageRenderer
    {
        public const int Width = 1200;
        public const int Height = 540;

        private static readonly Color BlueInk = Color.FromRgb( 26, 48, 125 );
        private static readonly Color BlackInk = Color.FromRgb( 28, 28, 34 );
        private static readonly Color Printed = Color.FromRgb( 35, 35, 40 );
        private static readonly Color Label = Color.FromRgb( 60, 64, 72 );

        /// <summary>
        /// Each bank's check paper: a light background tint, its security lines, the faint SAMPLE
        /// pattern, and the logo colors. The order matches <see cref="Wallet.Banks"/>.
        /// </summary>
        private static readonly (Color Paper, Color Wave, Color Faint, Color Logo, Color LogoDark)[] Palettes =
        {
            (Color.FromRgb( 226, 237, 248 ), Color.FromRgb( 212, 226, 242 ), Color.FromRgb( 214, 228, 243 ), Color.FromRgb( 120, 160, 205 ), Color.FromRgb( 40, 80, 140 )),   // light blue
            (Color.FromRgb( 228, 242, 229 ), Color.FromRgb( 213, 232, 215 ), Color.FromRgb( 215, 233, 217 ), Color.FromRgb( 125, 180, 130 ), Color.FromRgb( 45, 110, 60 )),   // light green
            (Color.FromRgb( 248, 236, 224 ), Color.FromRgb( 238, 222, 205 ), Color.FromRgb( 240, 224, 208 ), Color.FromRgb( 205, 140, 90 ), Color.FromRgb( 140, 75, 35 )),   // light copper
            (Color.FromRgb( 225, 241, 240 ), Color.FromRgb( 209, 230, 229 ), Color.FromRgb( 211, 232, 231 ), Color.FromRgb( 100, 170, 165 ), Color.FromRgb( 30, 105, 100 )),  // light teal
            (Color.FromRgb( 237, 232, 246 ), Color.FromRgb( 224, 217, 238 ), Color.FromRgb( 226, 219, 239 ), Color.FromRgb( 155, 135, 195 ), Color.FromRgb( 85, 65, 135 ))   // light lavender
        };

        private readonly Dictionary<(string Bank, bool IsBusiness), Image<Rgb24>> _fronts = new Dictionary<(string, bool), Image<Rgb24>>();
        private readonly Image<Rgb24> _back;
        private readonly JpegEncoder _encoder = new JpegEncoder { Quality = 55, ColorType = JpegColorType.YCbCrRatio420 };

        private readonly Font _printed;
        private readonly Font _printedBold;
        private readonly Font _businessName;
        private readonly Font _typed;
        private readonly Font _small;
        private readonly Font _tiny;
        private readonly Font _bankName;
        private readonly Font _sample;
        private readonly Font _stamp;
        private readonly List<(Font Large, Font Small)> _hands;
        private readonly List<Font> _signatures;

        /// <param name="files">The package (or repository) that holds content/fonts.</param>
        public CheckImageRenderer( IPackageFiles files )
        {
            // Each image is drawn on one thread; many images draw in parallel instead.
            Configuration.Default.MaxDegreeOfParallelism = 1;

            var fonts = new FontCollection();
            const string contentFonts = "content/fonts";
            const string openSansFolder = contentFonts;
            FontFamily Load( string folder, string file )
            {
                using var stream = new MemoryStream( files.ReadBytes( $"{folder}/{file}" ) );
                return fonts.Add( stream );
            }

            var openSans = Load( openSansFolder, "OpenSans-Regular-webfont.ttf" );
            var openSansBold = Load( openSansFolder, "OpenSans-Bold-webfont.ttf" );
            var openSansSemibold = Load( openSansFolder, "OpenSans-Semibold-webfont.ttf" );
            var caveat = Load( contentFonts, "Caveat-Variable.ttf" );
            var patrickHand = Load( contentFonts, "PatrickHand-Regular.ttf" );
            var kalam = Load( contentFonts, "Kalam-Regular.ttf" );
            var homemadeApple = Load( contentFonts, "HomemadeApple-Regular.ttf" );
            var saintDelafield = Load( contentFonts, "MrsSaintDelafield-Regular.ttf" );

            _printed = openSans.CreateFont( 16 );
            _printedBold = openSansBold.CreateFont( 16, FontStyle.Bold );
            _businessName = openSansBold.CreateFont( 22, FontStyle.Bold );
            _typed = openSansSemibold.CreateFont( 18 );
            _small = openSans.CreateFont( 13 );
            _tiny = openSans.CreateFont( 10 );
            _bankName = openSansBold.CreateFont( 18, FontStyle.Bold );
            _sample = openSansBold.CreateFont( 54, FontStyle.Bold );
            _stamp = openSansBold.CreateFont( 16, FontStyle.Bold );

            // Open Sans files share one family name, so bold is asked for by style.
            // Each font has its own natural size, so each gets a size that reads the same on the check.
            _hands = new List<(Font, Font)>
            {
                (caveat.CreateFont( 32 ), caveat.CreateFont( 28 )),
                (patrickHand.CreateFont( 27 ), patrickHand.CreateFont( 23 )),
                (kalam.CreateFont( 24 ), kalam.CreateFont( 21 ))
            };
            _signatures = new List<Font> { homemadeApple.CreateFont( 25 ), saintDelafield.CreateFont( 50 ) };

            for ( var i = 0; i < Wallet.Banks.Length; i++ )
            {
                var palette = Palettes[i % Palettes.Length];
                _fronts[(Wallet.Banks[i], false)] = DrawFront( Wallet.Banks[i], i, palette, isBusiness: false );
                _fronts[(Wallet.Banks[i], true)] = DrawFront( Wallet.Banks[i], i, palette, isBusiness: true );
            }

            _back = DrawBack();
        }

        /// <summary>
        /// Draws one check. Safe to call from many threads at once.
        /// </summary>
        public (byte[] Front, byte[] Back) Render( CheckImageData check )
        {
            Image<Rgb24> template;
            lock ( _fronts )
            {
                template = _fronts[(check.Bank, check.IsBusiness)];
            }

            using var front = template.Clone( x =>
            {
                if ( check.IsBusiness )
                {
                    DrawBusinessDetails( x, check );
                }
                else
                {
                    DrawPersonalDetails( x, check );
                }

                // The bank line: transit, routing, transit, account, on-us, check number.
                DrawMicr( x, $"T{check.RoutingNumber}T {check.AccountNumber}U {check.CheckNumber:D4}", new PointF( 160, 478 ), 2.5f );
            } );

            using var back = _back.Clone( x =>
            {
                // The church's endorsement stamp, turned the way a stamp is pressed onto a check back.
                var stampAt = Matrix3x2.CreateRotation( -( float ) Math.PI / 2 + ( ( check.HandwritingIndex % 5 ) - 2 ) * 0.02f, new Vector2( 150, 300 ) );
                var stampText = $"FOR DEPOSIT ONLY\n{check.Payee.ToUpperInvariant()}\nACCT ****{check.DepositAccountLast4}";
                x.DrawText( new DrawingOptions { Transform = stampAt }, stampText, _stamp, Color.FromRgb( 70, 70, 120 ), new PointF( 40, 250 ) );

                // The bank's processing line, printed across the check when it was deposited.
                var processingAt = Matrix3x2.CreateRotation( -( float ) Math.PI / 2, new Vector2( 640, 470 ) );
                var processing = $"{check.DepositDate:MMddyyyy} {check.DepositBank.ToUpperInvariant()} >{check.RoutingNumber}< {check.SequenceNumber:D10}";
                x.DrawText( new DrawingOptions { Transform = processingAt }, processing, _small, Color.FromRgb( 90, 90, 90 ), new PointF( 640, 470 ) );
            } );

            return (Encode( front ), Encode( back ));
        }

        /// <summary>
        /// A personal check: printed name and address, everything else handwritten.
        /// </summary>
        private void DrawPersonalDetails( IImageProcessingContext x, CheckImageData check )
        {
            var (hand, handSmall) = _hands[check.HandwritingIndex % _hands.Count];
            var signature = _signatures[( check.HandwritingIndex / 3 ) % _signatures.Count];
            var ink = check.HandwritingIndex % 5 < 3 ? BlueInk : BlackInk;
            var words = AmountInWords( check.Amount );

            for ( var i = 0; i < check.WriterLines.Count; i++ )
            {
                x.DrawText( check.WriterLines[i], i == 0 ? _printedBold : _small, Printed, new PointF( 44, 30 + ( i == 0 ? 0 : 6 + ( i * 19 ) ) ) );
            }

            x.DrawText( check.CheckNumber.ToString(), _printedBold, Printed, new PointF( 1085, 30 ) );
            x.DrawText( FractionalRouting( check.RoutingNumber ), _tiny, Printed, new PointF( 1074, 56 ) );

            x.DrawText( WrittenDate( check.WrittenDate, check.HandwritingIndex ), hand, ink, new PointF( 905, 94 ) );
            x.DrawText( check.Payee, hand, ink, new PointF( 205, 164 ) );
            x.DrawText( check.Amount.ToString( "#,##0.00" ), hand, ink, new PointF( 1000, 164 ) );
            x.DrawText( words, handSmall, ink, new PointF( 52, 234 ) );
            var wordsWidth = TextMeasurer.MeasureSize( words, new TextOptions( handSmall ) ).Width;
            if ( wordsWidth < 860 )
            {
                x.DrawLine( Pens.Solid( ink, 2 ), new PointF( 70 + wordsWidth, 262 ), new PointF( 940, 262 ) );
            }

            if ( !string.IsNullOrEmpty( check.Memo ) )
            {
                x.DrawText( check.Memo, handSmall, ink, new PointF( 118, 396 ) );
            }

            x.DrawText( check.Signature, signature, ink, new PointF( 750, 372 ) );
        }

        /// <summary>
        /// A business check: printed by accounting software, then signed by hand.
        /// </summary>
        private void DrawBusinessDetails( IImageProcessingContext x, CheckImageData check )
        {
            var signature = _signatures[check.HandwritingIndex % _signatures.Count];
            var ink = check.HandwritingIndex % 2 == 0 ? BlueInk : BlackInk;

            x.DrawText( check.WriterLines[0], _businessName, Printed, new PointF( 44, 26 ) );
            for ( var i = 1; i < check.WriterLines.Count; i++ )
            {
                x.DrawText( check.WriterLines[i], _small, Printed, new PointF( 44, 40 + ( i * 19 ) ) );
            }

            x.DrawText( $"No. {check.CheckNumber}", _printedBold, Printed, new PointF( 1060, 30 ) );
            x.DrawText( FractionalRouting( check.RoutingNumber ), _tiny, Printed, new PointF( 1074, 56 ) );

            x.DrawText( check.WrittenDate.ToString( "MM/dd/yyyy" ), _typed, Printed, new PointF( 905, 102 ) );
            x.DrawText( $"****{AmountInWords( check.Amount ).ToUpperInvariant()} DOLLARS", _typed, Printed, new PointF( 110, 176 ) );
            x.DrawText( $"$*****{check.Amount:#,##0.00}", _typed, Printed, new PointF( 985, 176 ) );
            x.DrawText( check.Payee.ToUpperInvariant(), _typed, Printed, new PointF( 205, 238 ) );

            if ( !string.IsNullOrEmpty( check.Memo ) )
            {
                x.DrawText( check.Memo, _typed, Printed, new PointF( 118, 404 ) );
            }

            x.DrawText( check.Signature, signature, ink, new PointF( 760, 372 ) );
        }

        /// <summary>
        /// Writes a check amount the way people write it: "Two hundred fifty and 00/100".
        /// </summary>
        public static string AmountInWords( decimal amount )
        {
            var dollars = ( long ) Math.Floor( amount );
            var cents = ( int ) Math.Round( ( amount - dollars ) * 100 );
            var words = dollars == 0 ? "Zero" : NumberWords( dollars );
            return $"{char.ToUpperInvariant( words[0] )}{words.Substring( 1 )} and {cents:D2}/100";
        }

        private static string NumberWords( long number )
        {
            string[] ones = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
            string[] tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };
            var parts = new List<string>();
            if ( number >= 1000000 )
            {
                parts.Add( NumberWords( number / 1000000 ) + " million" );
                number %= 1000000;
            }

            if ( number >= 1000 )
            {
                parts.Add( NumberWords( number / 1000 ) + " thousand" );
                number %= 1000;
            }

            if ( number >= 100 )
            {
                parts.Add( ones[number / 100] + " hundred" );
                number %= 100;
            }

            if ( number >= 20 )
            {
                parts.Add( tens[number / 10] + ( number % 10 > 0 ? "-" + ones[number % 10] : string.Empty ) );
            }
            else if ( number > 0 )
            {
                parts.Add( ones[number] );
            }

            return string.Join( " ", parts );
        }

        /// <summary>
        /// The small routing fraction printed under the check number, for example 39-1982/357.
        /// </summary>
        private static string FractionalRouting( string routingNumber )
        {
            return $"{routingNumber.Substring( 0, 2 )}-{routingNumber.Substring( 2, 4 )}/{routingNumber.Substring( 6 )}";
        }

        /// <summary>
        /// People write dates in their own way; each writer keeps one style.
        /// </summary>
        private static string WrittenDate( DateTime date, int handwritingIndex )
        {
            return ( handwritingIndex % 3 ) switch
            {
                0 => date.ToString( "M/d/yy" ),
                1 => date.ToString( "MMM d, yyyy" ),
                _ => date.ToString( "M-d-yyyy" )
            };
        }

        private byte[] Encode( Image image )
        {
            using var stream = new MemoryStream();
            image.Save( stream, _encoder );
            return stream.ToArray();
        }

        /// <summary>
        /// Draws one bank's blank check: security pattern, logo, labels, and lines.
        /// Business checks put the bank at the top and add an authorized signature line.
        /// </summary>
        private Image<Rgb24> DrawFront( string bank, int bankIndex, (Color Paper, Color Wave, Color Faint, Color Logo, Color LogoDark) palette, bool isBusiness )
        {
            var image = new Image<Rgb24>( Width, Height, palette.Paper );
            image.Mutate( x =>
            {
                // Fine wavy security lines, different for each bank.
                var wave = Pens.Solid( palette.Wave, 1 );
                for ( var y = -50; y < Height + 50; y += 10 )
                {
                    var points = Enumerable.Range( 0, 61 ).Select( k => new PointF( k * 20, y + ( float ) ( 9 * Math.Sin( ( k * 0.35 ) + ( y * 0.04 ) + bankIndex ) ) ) ).ToArray();
                    x.DrawLine( wave, points );
                }

                // A faint "SAMPLE" pattern across the paper, like a void pantograph.
                for ( var row = 0; row < 3; row++ )
                {
                    for ( var col = 0; col < 4; col++ )
                    {
                        x.DrawText( "SAMPLE", _sample, palette.Faint, new PointF( 40 + ( col * 300 ) + ( row * 70 ), 110 + ( row * 140 ) ) );
                    }
                }

                var line = Pens.Solid( Color.FromRgb( 70, 75, 85 ), 2 );
                x.Draw( Pens.Solid( Color.FromRgb( 120, 128, 135 ), 3 ), new RectangleF( 8, 8, Width - 16, Height - 16 ) );

                if ( isBusiness )
                {
                    // A colored band across the top, as on most business checks.
                    x.Fill( palette.Logo.WithAlpha( 0.35f ), new RectangleF( 11, 11, Width - 22, 12 ) );
                    DrawLogo( x, bankIndex, palette, new PointF( 560, 38 ), 0.8f );
                    x.DrawText( bank, _bankName, palette.LogoDark, new PointF( 640, 34 ) );
                    x.DrawText( "Phoenix, Arizona", _tiny, Label, new PointF( 640, 60 ) );

                    x.DrawText( "DATE", _tiny, Label, new PointF( 850, 110 ) );
                    x.DrawLine( line, new PointF( 895, 130 ), new PointF( 1140, 130 ) );
                    x.DrawText( "PAY", _tiny, Label, new PointF( 44, 180 ) );
                    x.DrawLine( line, new PointF( 100, 205 ), new PointF( 960, 205 ) );
                    x.Draw( line, new RectangleF( 975, 166, 190, 46 ) );
                    x.DrawText( "TO THE\nORDER OF", _tiny, Label, new PointF( 44, 240 ) );
                    x.DrawLine( line, new PointF( 135, 268 ), new PointF( 700, 268 ) );
                    x.DrawText( "MEMO", _tiny, Label, new PointF( 44, 416 ) );
                    x.DrawLine( line, new PointF( 100, 432 ), new PointF( 560, 432 ) );
                    x.DrawLine( line, new PointF( 720, 432 ), new PointF( 1160, 432 ) );
                    x.DrawText( "AUTHORIZED SIGNATURE", _tiny, Label, new PointF( 870, 440 ) );
                }
                else
                {
                    DrawLogo( x, bankIndex, palette, new PointF( 46, 300 ), 1f );
                    x.DrawText( bank, _bankName, palette.LogoDark, new PointF( 142, 304 ) );
                    x.DrawText( "Phoenix, Arizona", _tiny, Label, new PointF( 142, 332 ) );

                    x.DrawText( "DATE", _tiny, Label, new PointF( 850, 116 ) );
                    x.DrawLine( line, new PointF( 895, 135 ), new PointF( 1140, 135 ) );
                    x.DrawText( "PAY TO THE\nORDER OF", _tiny, Label, new PointF( 44, 176 ) );
                    x.DrawLine( line, new PointF( 135, 205 ), new PointF( 960, 205 ) );
                    x.Draw( line, new RectangleF( 975, 160, 190, 50 ) );
                    x.DrawText( "$", _bankName, Label, new PointF( 982, 170 ) );
                    x.DrawLine( line, new PointF( 44, 275 ), new PointF( 1040, 275 ) );
                    x.DrawText( "DOLLARS", _tiny, Label, new PointF( 1050, 264 ) );
                    x.DrawText( "MEMO", _tiny, Label, new PointF( 44, 416 ) );
                    x.DrawLine( line, new PointF( 100, 432 ), new PointF( 560, 432 ) );
                    x.DrawLine( line, new PointF( 720, 432 ), new PointF( 1160, 432 ) );
                    x.DrawText( "Security features included. Details on back.", _tiny, Label, new PointF( 870, 440 ) );
                }
            } );

            return image;
        }

        /// <summary>
        /// Draws each bank's own logo in an 84 by 48 box (at scale 1), in the bank's colors.
        /// </summary>
        private static void DrawLogo( IImageProcessingContext x, int bankIndex, (Color Paper, Color Wave, Color Faint, Color Logo, Color LogoDark) palette, PointF at, float scale )
        {
            PointF P( float px, float py ) => new PointF( at.X + ( px * scale ), at.Y + ( py * scale ) );
            var light = palette.Logo;
            var dark = palette.LogoDark;

            switch ( bankIndex % 5 )
            {
                case 0:
                    // Desert Sky: a sun rising over a ridge.
                    x.Fill( light, new EllipsePolygon( P( 36, 20 ), 18 * scale ) );
                    x.Fill( dark, new Polygon( new LinearLineSegment( P( 0, 46 ), P( 26, 18 ), P( 42, 33 ), P( 58, 12 ), P( 84, 46 ) ) ) );
                    break;

                case 1:
                    // Ocotillo: tall, thin canes fanning up from one base, with blooms at the tips.
                    for ( var k = -2; k <= 2; k++ )
                    {
                        var tipX = 42 + ( k * 15 );
                        var tipY = 4 + ( Math.Abs( k ) * 6 );
                        x.DrawLine( Pens.Solid( dark, 3.2f * scale ), P( 42, 47 ), P( tipX, tipY ) );
                        x.Fill( light, new EllipsePolygon( P( tipX, tipY ), 4 * scale ) );
                    }

                    break;

                case 2:
                    // Copper Wash: a copper coin with water running across it.
                    x.Fill( light, new EllipsePolygon( P( 42, 24 ), 23 * scale ) );
                    x.Draw( Pens.Solid( dark, 2.5f * scale ), new EllipsePolygon( P( 42, 24 ), 23 * scale ) );
                    for ( var k = 0; k < 3; k++ )
                    {
                        var y = 15 + ( k * 9 );
                        var wavePoints = Enumerable.Range( 0, 9 ).Select( i => P( 24 + ( i * 4.5f ), y + ( float ) ( 2.5 * Math.Sin( i * 1.3 ) ) ) ).ToArray();
                        x.DrawLine( Pens.Solid( dark, 2.2f * scale ), wavePoints );
                    }

                    break;

                case 3:
                    // Agave: a rosette of pointed leaves.
                    var leaves = new[] { (-40f, 30f), (-24f, 10f), (0f, 0f), (24f, 10f), (40f, 30f) };
                    foreach ( var (dx, dy) in leaves )
                    {
                        var color = Math.Abs( dx ) > 30 ? light : dark;
                        x.Fill( color, new Polygon( new LinearLineSegment( P( 42 - 7, 47 ), P( 42 + dx, dy ), P( 42 + 7, 47 ) ) ) );
                    }

                    break;

                default:
                    // Javelina Valley: a shield with a valley line through it.
                    x.Fill( dark, new Polygon( new LinearLineSegment( P( 22, 2 ), P( 62, 2 ), P( 62, 26 ), P( 42, 47 ), P( 22, 26 ) ) ) );
                    x.Fill( light, new Polygon( new LinearLineSegment( P( 26, 22 ), P( 36, 12 ), P( 42, 18 ), P( 50, 9 ), P( 58, 22 ), P( 58, 25 ), P( 42, 42 ), P( 26, 25 ) ) ) );
                    break;
            }
        }

        /// <summary>
        /// Draws the blank check back: endorsement lines and the usual warning.
        /// </summary>
        private Image<Rgb24> DrawBack()
        {
            var image = new Image<Rgb24>( Width, Height, Color.FromRgb( 242, 243, 241 ) );
            image.Mutate( x =>
            {
                var line = Pens.Solid( Color.FromRgb( 160, 160, 160 ), 1 );
                var label = Color.FromRgb( 110, 110, 110 );
                for ( var k = 0; k < 4; k++ )
                {
                    x.DrawLine( line, new PointF( 70 + ( k * 46 ), 40 ), new PointF( 70 + ( k * 46 ), 500 ) );
                }

                x.DrawText( "ENDORSE HERE", _tiny, label, new PointF( 40, 18 ) );
                x.DrawText( "X", _small, label, new PointF( 44, 470 ) );
                x.DrawText( "DO NOT WRITE, STAMP, OR SIGN BELOW THIS LINE", _tiny, label, new PointF( 300, 18 ) );
                x.DrawText( "RESERVED FOR FINANCIAL INSTITUTION USE", _tiny, label, new PointF( 300, 34 ) );
                x.DrawText( "This check has security features, including a void pattern and microprint.", _tiny, label, new PointF( 700, 505 ) );
            } );

            return image;
        }

        /// <summary>
        /// Draws the bank line in E-13B style from simple blocks. "T" is the transit symbol and "U" the on-us symbol.
        /// </summary>
        private static void DrawMicr( IImageProcessingContext x, string text, PointF origin, float unit )
        {
            var cursor = origin.X;
            foreach ( var c in text )
            {
                if ( c == ' ' )
                {
                    cursor += unit * 5;
                    continue;
                }

                if ( MicrGlyphs.TryGetValue( c, out var blocks ) )
                {
                    foreach ( var b in blocks )
                    {
                        x.Fill( Color.Black, new RectangleF( cursor + ( b.X * unit ), origin.Y + ( b.Y * unit ), b.W * unit, b.H * unit ) );
                    }
                }

                cursor += unit * 8;
            }
        }

        /// <summary>
        /// Block shapes for E-13B digits and symbols on a 7 by 9 grid.
        /// </summary>
        private static readonly Dictionary<char, (float X, float Y, float W, float H)[]> MicrGlyphs = new Dictionary<char, (float, float, float, float)[]>
        {
            ['0'] = new[] { (0f, 0f, 6f, 1f), (0f, 0f, 2f, 9f), (4.5f, 0f, 1.5f, 9f), (0f, 8f, 6f, 1f) },
            ['1'] = new[] { (2.5f, 0f, 2.2f, 9f), (1f, 6.5f, 4.5f, 2.5f) },
            ['2'] = new[] { (0.5f, 0f, 5.5f, 1f), (4.5f, 0f, 1.5f, 5f), (0.5f, 4f, 5.5f, 1f), (0.5f, 4f, 2.5f, 5f), (0.5f, 8f, 5.5f, 1f) },
            ['3'] = new[] { (0.5f, 0f, 5f, 1f), (4f, 0f, 1.5f, 4.5f), (1.5f, 3.5f, 4f, 1.2f), (3.5f, 3.5f, 2.5f, 5.5f), (0.5f, 8f, 5.5f, 1f) },
            ['4'] = new[] { (0.5f, 0f, 1.5f, 6f), (0.5f, 5f, 5.5f, 1f), (3.5f, 2.5f, 2.5f, 6.5f) },
            ['5'] = new[] { (0.5f, 0f, 5.5f, 1f), (0.5f, 0f, 1.5f, 4.5f), (0.5f, 3.5f, 5.5f, 1.2f), (3.5f, 3.5f, 2.5f, 5.5f), (0.5f, 8f, 5.5f, 1f) },
            ['6'] = new[] { (0f, 0f, 2.5f, 9f), (0f, 0f, 5f, 1f), (0f, 4f, 6f, 1f), (4.5f, 4f, 1.5f, 5f), (0f, 8f, 6f, 1f) },
            ['7'] = new[] { (0.5f, 0f, 5.5f, 1f), (4.5f, 0f, 1.5f, 4f), (2.5f, 3.5f, 2.5f, 5.5f) },
            ['8'] = new[] { (1f, 0f, 4f, 1f), (1f, 0f, 1f, 4f), (4f, 0f, 1f, 4f), (0.5f, 3.5f, 5f, 1f), (0f, 3.5f, 2.2f, 5.5f), (4f, 3.5f, 2f, 5.5f), (0f, 8f, 6f, 1f) },
            ['9'] = new[] { (0f, 0f, 6f, 1f), (0f, 0f, 1.5f, 5f), (0f, 4f, 6f, 1f), (3.5f, 0f, 2.5f, 9f), (1f, 8f, 5f, 1f) },
            ['T'] = new[] { (0f, 1f, 1.2f, 7f), (2.5f, 1f, 2.5f, 2f), (2.5f, 6f, 2.5f, 2f) },
            ['U'] = new[] { (0f, 1f, 1.2f, 5.5f), (2.2f, 1f, 1.2f, 5.5f), (4.4f, 1f, 1.2f, 5.5f), (0f, 7f, 5.6f, 1.2f) }
        };
    }
}
