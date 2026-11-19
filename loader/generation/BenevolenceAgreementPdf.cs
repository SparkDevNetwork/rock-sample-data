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

namespace RockSampleData.Loader.Generation
{
    /// <summary>
    /// Draws a benevolence request's assistance agreement: the terms, the request, the help the church gives, and the
    /// typed names of the requester and the case worker who signed it.
    /// </summary>
    internal static class BenevolenceAgreementPdf
    {
        public static byte[] Draw( string title, string organization, IReadOnlyList<string> paragraphs, string requester, string requestType, DateTime requested,
            IReadOnlyList<(string Help, decimal? Amount)> help, string caseWorker, DateTime signed )
        {
            var page = new PdfPages();
            page.Paragraph( title, PdfFont.Bold, 18, 24, 2 );
            page.Paragraph( organization, PdfFont.Regular, 11, 15, 10 );
            page.Rule( 14 );

            page.LabelValue( "Requester:", requester, 11, 16 );
            page.LabelValue( "Request:", requestType, 11, 16 );
            page.LabelValue( "Requested:", Day( requested ), 11, 16 );
            page.Y -= 10;

            foreach ( var paragraph in paragraphs )
            {
                page.Paragraph( paragraph.Replace( "{church}", organization ), PdfFont.Regular, 11, 15, 8 );
            }

            page.Y -= 4;
            page.Rule( 4 );
            var amountColumn = PdfPages.Margin + 360;
            page.Row( new[] { (PdfPages.Margin, "Help the church gives"), (amountColumn, "Amount") }, PdfFont.Bold, 11, 16 );
            foreach ( var (text, amount) in help )
            {
                page.Row( new[] { (PdfPages.Margin, text), (amountColumn, amount.HasValue ? amount.Value.ToString( "C", CultureInfo.GetCultureInfo( "en-US" ) ) : "None") }, PdfFont.Regular, 11, 16 );
            }

            page.Y -= 8;
            page.Rule( 18 );
            page.LabelValue( "Signed by the requester:", requester, 11, 18 );
            page.LabelValue( "Signed by the case worker:", caseWorker, 11, 18 );
            page.LabelValue( "Date:", Day( signed ), 11, 18 );
            page.Y -= 14;
            page.Paragraph( "This agreement is sample data for Rock RMS. The people and amounts are fictional.", PdfFont.Regular, 9, 12, 0 );
            return page.Save( $"{title}: {requester}" );
        }

        private static string Day( DateTime day ) => day.ToString( "MMMM d, yyyy", CultureInfo.InvariantCulture );
    }
}
