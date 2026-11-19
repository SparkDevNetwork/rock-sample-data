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
    /// Draws a finished background check's report: the candidate, the package, each search and its result, and a note
    /// that it is sample data. A Fail shows one record for the church's safety team to review.
    /// </summary>
    internal static class BackgroundCheckReportPdf
    {
        /// <summary>
        /// Draws the report. When the check failed, the last search found the record.
        /// </summary>
        public static byte[] Draw( string organization, string candidate, string reportKey, string packageTitle, IReadOnlyList<string> searches, DateTime requested, DateTime completed, bool isPass )
        {
            var page = new PdfPages();
            page.Paragraph( "Background Check Report", PdfFont.Bold, 18, 24, 2 );
            page.Paragraph( $"Prepared for {organization}", PdfFont.Regular, 11, 15, 10 );
            page.Rule( 14 );

            page.LabelValue( "Candidate:", candidate, 11, 16 );
            page.LabelValue( "Report:", reportKey, 11, 16 );
            page.LabelValue( "Package:", packageTitle, 11, 16 );
            page.LabelValue( "Requested:", Day( requested ), 11, 16 );
            page.LabelValue( "Completed:", Day( completed ), 11, 16 );
            page.LabelValue( "Status:", isPass ? "Clear" : "Consider", 11, 16 );
            page.Y -= 12;

            page.Rule( 4 );
            var resultColumn = PdfPages.Margin + 300;
            page.Row( new[] { (PdfPages.Margin, "Search"), (resultColumn, "Result") }, PdfFont.Bold, 11, 16 );
            for ( var i = 0; i < searches.Count; i++ )
            {
                var found = !isPass && i == searches.Count - 1;
                page.Row( new[] { (PdfPages.Margin, searches[i]), (resultColumn, found ? "Consider: 1 record" : "Clear") }, PdfFont.Regular, 11, 16 );
            }

            page.Y -= 8;
            page.Rule( 14 );
            page.Paragraph( isPass
                    ? "No records were found. The candidate may serve where the church requires a background check."
                    : "One misdemeanor record was found. The church's safety team reviews every record with the candidate before deciding where they may serve.",
                PdfFont.Regular, 11, 15, 18 );
            page.Paragraph( "This report is sample data for Rock RMS. The candidate and every result are fictional.", PdfFont.Regular, 9, 12, 0 );
            return page.Save( $"Background Check Report: {candidate}" );
        }

        private static string Day( DateTime day ) => day.ToString( "MMMM d, yyyy", CultureInfo.InvariantCulture );
    }
}
