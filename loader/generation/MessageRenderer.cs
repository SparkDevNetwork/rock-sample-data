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
using System.Text.RegularExpressions;

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Turns a library message into what Rock stores: tokens filled for its occasion and campus, Markdown
    /// links on the church's site, and the body inside the branded template's drop zone. Rock's own Lava
    /// (the template's colors and {{ Person.NickName }}) is left for Rock to fill at send time.
    /// </summary>
    internal sealed class MessageRenderer
    {
        public const string Site = "https://rocksolidchurchdemo.com";

        private static readonly Regex Link = new Regex( @"\[([^\]]+)\]\(([^)]+)\)" );
        private static readonly Regex TextWrapper = new Regex( @"(<td class=""js-component-text-wrapper""[^>]*>)(.*?)(</td>)", RegexOptions.Singleline );
        private static readonly Regex Preheader = new Regex( @"(<div id=""preheader-text""[^>]*>)(</div>)" );

        private readonly string _template;

        /// <param name="template">The template's HTML as Rock stores it, with its logo in place.</param>
        public MessageRenderer( string template )
        {
            if ( !TextWrapper.IsMatch( template ) || !Preheader.IsMatch( template ) )
            {
                throw new InvalidDataException( "The email template needs a js-component-text-wrapper cell and an empty preheader-text div." );
            }

            _template = template;
        }

        /// <summary>
        /// The tokens for one occasion at one campus. Null series values mean no series is running.
        /// </summary>
        public static Dictionary<string, string> Tokens( DateTime eventDate, DateTime sendDate, string campusName, string campusKey, string serviceTimes, (SermonSeries Series, int Week)? series )
        {
            return new Dictionary<string, string>
            {
                ["{eventDate}"] = LongDate( eventDate ),
                ["{eventDay}"] = eventDate.ToString( "dddd", CultureInfo.InvariantCulture ),
                ["{eventYear}"] = eventDate.Year.ToString( CultureInfo.InvariantCulture ),
                ["{registerBy}"] = LongDate( eventDate.AddDays( -14 ) ),
                ["{sendDate}"] = LongDate( sendDate ),
                ["{campus}"] = campusName,
                ["{campusKey}"] = campusKey,
                ["{serviceTimes}"] = serviceTimes,
                ["{series}"] = series?.Series.Name ?? string.Empty,
                ["{seriesTagline}"] = series?.Series.Tagline ?? string.Empty,
                ["{seriesWeek}"] = series?.Week.ToString( CultureInfo.InvariantCulture ) ?? string.Empty
            };
        }

        /// <summary>
        /// A date the way the messages write it, such as "Sunday, April 5".
        /// </summary>
        public static string LongDate( DateTime date ) => date.ToString( "dddd, MMMM d", CultureInfo.InvariantCulture );

        /// <summary>
        /// "9:00 and 10:30 a.m." for a campus's service start times.
        /// </summary>
        public static string ServiceTimes( IEnumerable<TimeSpan> starts )
        {
            var times = starts.OrderBy( t => t ).ToList();
            if ( times.Count == 0 )
            {
                return string.Empty;
            }

            string Clock( TimeSpan t ) => $"{( t.Hours % 12 == 0 ? 12 : t.Hours % 12 )}:{t.Minutes:00}";
            string Half( TimeSpan t ) => t.Hours < 12 ? "a.m." : "p.m.";
            var parts = new List<string>();
            for ( var i = 0; i < times.Count; i++ )
            {
                var isLastOfHalf = i == times.Count - 1 || Half( times[i + 1] ) != Half( times[i] );
                parts.Add( isLastOfHalf ? $"{Clock( times[i] )} {Half( times[i] )}" : Clock( times[i] ) );
            }

            return parts.Count == 1 ? parts[0]
                : parts.Count == 2 ? $"{parts[0]} and {parts[1]}"
                : $"{string.Join( ", ", parts.Take( parts.Count - 1 ) )}, and {parts[parts.Count - 1]}";
        }

        /// <summary>
        /// Fills the load-time tokens.
        /// </summary>
        public static string Fill( string text, IReadOnlyDictionary<string, string> tokens )
        {
            foreach ( var (token, value) in tokens )
            {
                text = text.Replace( token, value );
            }

            return text;
        }

        /// <summary>
        /// Renders an email's body inside the template. Returns the message and its links, in order.
        /// </summary>
        public (string Html, List<string> Links) Render( IEnumerable<EmailBlock> blocks, string preheader, IReadOnlyDictionary<string, string> tokens )
        {
            var links = new List<string>();
            var body = new StringBuilder();
            var isFirst = true;
            foreach ( var block in blocks )
            {
                if ( block.H != null )
                {
                    var size = isFirst ? "font-size: 28px; line-height: 34px;" : "font-size: 21px; line-height: 28px;";
                    body.Append( $"<h{( isFirst ? 1 : 2 )} style=\"margin: 0 0 16px 0; color: {{{{ textColor }}}}; {size} font-weight: 600; letter-spacing: -0.3px;\">{Inline( block.H, tokens, links )}</h{( isFirst ? 1 : 2 )}>" );
                }
                else if ( block.P != null )
                {
                    body.Append( $"<p style=\"margin: 0 0 16px 0; color: {{{{ textColor }}}}; font-size: 17px; line-height: 26px;\">{Inline( block.P, tokens, links )}</p>" );
                }
                else if ( block.List != null )
                {
                    body.Append( "<ul style=\"margin: 0 0 16px 0; padding: 0 0 0 22px; color: {{ textColor }}; font-size: 17px; line-height: 26px;\">" );
                    foreach ( var item in block.List )
                    {
                        body.Append( $"<li style=\"margin: 0 0 6px 0;\">{Inline( item, tokens, links )}</li>" );
                    }

                    body.Append( "</ul>" );
                }
                else if ( block.Button != null )
                {
                    body.Append( Button( Fill( block.Button, tokens ), Url( block.Href, tokens, links ) ) );
                }

                isFirst = false;
            }

            var html = TextWrapper.Replace( _template, m => m.Groups[1].Value + body + m.Groups[3].Value, 1 );
            html = Preheader.Replace( html, m => m.Groups[1].Value + Escape( Fill( preheader ?? string.Empty, tokens ) ) + m.Groups[2].Value, 1 );
            return (html, links);
        }

        /// <summary>
        /// Renders an email's body as plain HTML with no template, the way a one-to-one email is stored. A button
        /// becomes a link. Returns the message and its links, in order.
        /// </summary>
        public static (string Html, List<string> Links) Compact( IEnumerable<EmailBlock> blocks, IReadOnlyDictionary<string, string> tokens )
        {
            var links = new List<string>();
            var body = new StringBuilder();
            foreach ( var block in blocks )
            {
                if ( block.H != null )
                {
                    body.Append( $"<h2>{Inline( block.H, tokens, links ).Replace( " style=\"color: {{ accentColor }}; text-decoration: underline;\"", string.Empty )}</h2>" );
                }
                else if ( block.P != null )
                {
                    body.Append( $"<p>{Inline( block.P, tokens, links ).Replace( " style=\"color: {{ accentColor }}; text-decoration: underline;\"", string.Empty )}</p>" );
                }
                else if ( block.List != null )
                {
                    body.Append( "<ul>" );
                    foreach ( var item in block.List )
                    {
                        body.Append( $"<li>{Inline( item, tokens, links ).Replace( " style=\"color: {{ accentColor }}; text-decoration: underline;\"", string.Empty )}</li>" );
                    }

                    body.Append( "</ul>" );
                }
                else if ( block.Button != null )
                {
                    body.Append( $"<p><a href=\"{Url( block.Href, tokens, links )}\">{Escape( Fill( block.Button, tokens ) )}</a></p>" );
                }
            }

            return (body.ToString(), links);
        }

        /// <summary>
        /// A button that holds its shape in Outlook: the color and padding sit on the table cell.
        /// </summary>
        public static string Button( string text, string url )
        {
            return "<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"border-collapse: separate; margin: 8px 0 16px 0;\"><tr>"
                + "<td align=\"center\" bgcolor=\"{{ accentColor }}\" style=\"background-color: {{ accentColor }}; border-radius: 22px; padding: 12px 26px;\">"
                + $"<a href=\"{url}\" style=\"color: #FFFFFF; font-size: 16px; line-height: 20px; font-weight: 600; text-decoration: none; display: inline-block;\">{Escape( text )}</a>"
                + "</td></tr></table>";
        }

        /// <summary>
        /// Escapes text and turns its Markdown links into links on the church's site.
        /// </summary>
        private static string Inline( string text, IReadOnlyDictionary<string, string> tokens, List<string> links )
        {
            var filled = Fill( text, tokens );
            var html = new StringBuilder();
            var at = 0;
            foreach ( Match match in Link.Matches( filled ) )
            {
                html.Append( Escape( filled.Substring( at, match.Index - at ) ) );
                html.Append( $"<a href=\"{Url( match.Groups[2].Value, tokens, links )}\" style=\"color: {{{{ accentColor }}}}; text-decoration: underline;\">{Escape( match.Groups[1].Value )}</a>" );
                at = match.Index + match.Length;
            }

            html.Append( Escape( filled.Substring( at ) ) );
            return html.ToString();
        }

        private static string Url( string path, IReadOnlyDictionary<string, string> tokens, List<string> links )
        {
            var url = Site + Fill( path, tokens );
            links.Add( url );
            return url;
        }

        /// <summary>
        /// Escapes what HTML would read as markup. Rock's Lava merge fields pass through.
        /// </summary>
        public static string Escape( string text ) => text.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" ).Replace( "\"", "&quot;" );
    }
}
