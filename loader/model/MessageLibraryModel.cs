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

namespace RockSampleData.Loader.Model
{
    /*
        These classes mirror the message library in content/communications/ (its README describes
        every file). Property names match the JSON keys (case-insensitive).
    */

    /// <summary>
    /// content/communications/series.json: the sermon series and the slots they fill.
    /// </summary>
    internal sealed class SeriesLibrary
    {
        public Dictionary<string, SeriesSlot> Slots { get; set; } = new Dictionary<string, SeriesSlot>();

        public List<SermonSeries> Series { get; set; } = new List<SermonSeries>();

        public SermonSeries Campaign { get; set; }
    }

    internal sealed class SeriesSlot
    {
        public int Weeks { get; set; }
    }

    internal sealed class SermonSeries
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Tagline { get; set; }

        public string Slot { get; set; }

        /// <summary>
        /// The campaign series' length. Slot series take their slot's.
        /// </summary>
        public int Weeks { get; set; }
    }

    /// <summary>
    /// content/communications/newsletter.json: the pieces each campus's weekly newsletter is built from.
    /// </summary>
    internal sealed class NewsletterLibrary
    {
        public List<string> Subjects { get; set; } = new List<string>();

        public List<string> Openers { get; set; } = new List<string>();

        public List<string> Series { get; set; } = new List<string>();

        public List<NewsletterPiece> Pieces { get; set; } = new List<NewsletterPiece>();

        public List<NewsletterPiece> Evergreen { get; set; } = new List<NewsletterPiece>();

        public List<string> Closings { get; set; } = new List<string>();
    }

    /// <summary>
    /// One newsletter piece. An occasion piece runs while its occasion is <see cref="From"/> to
    /// <see cref="Until"/> days after the send. An evergreen piece has no occasion.
    /// </summary>
    internal sealed class NewsletterPiece
    {
        public string Key { get; set; }

        public string Occasion { get; set; }

        public int From { get; set; }

        public int Until { get; set; }

        public string Heading { get; set; }

        public string Text { get; set; }

        public string Button { get; set; }

        public string Href { get; set; }
    }

    /// <summary>
    /// content/communications/emails.json.
    /// </summary>
    internal sealed class EmailLibrary
    {
        public List<LibraryEmail> Emails { get; set; } = new List<LibraryEmail>();
    }

    /// <summary>
    /// One email in the library.
    /// </summary>
    internal sealed class LibraryEmail
    {
        public string Key { get; set; }

        public string Occasion { get; set; }

        /// <summary>
        /// Days before the occasion it is sent. Negative is after.
        /// </summary>
        public int DaysBefore { get; set; }

        /// <summary>
        /// newsletter, everyone, list:&lt;key&gt;, students-and-parents, class, or person.
        /// </summary>
        public string Audience { get; set; }

        public string Topic { get; set; }

        /// <summary>
        /// church, pastor, or students.
        /// </summary>
        public string Sender { get; set; }

        public string Subject { get; set; }

        public string Preheader { get; set; }

        public List<EmailBlock> Body { get; set; } = new List<EmailBlock>();

        /// <summary>
        /// False for a small leader send that is not flagged bulk.
        /// </summary>
        public bool Bulk { get; set; } = true;
    }

    /// <summary>
    /// One body block: a heading, a paragraph, a list, or a button.
    /// </summary>
    internal sealed class EmailBlock
    {
        public string H { get; set; }

        public string P { get; set; }

        public List<string> List { get; set; }

        public string Button { get; set; }

        public string Href { get; set; }
    }
}
