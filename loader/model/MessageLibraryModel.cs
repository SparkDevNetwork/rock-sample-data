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

    /// <summary>
    /// content/communications/texts.json: texts and pushes, and the reply pools for texts that ask for an answer.
    /// </summary>
    internal sealed class TextLibrary
    {
        public List<LibraryText> Texts { get; set; } = new List<LibraryText>();

        /// <summary>
        /// Short replies by kind: yes, no, question, and thanks.
        /// </summary>
        public Dictionary<string, List<string>> Replies { get; set; } = new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// One text or push in the library.
    /// </summary>
    internal sealed class LibraryText
    {
        public string Key { get; set; }

        /// <summary>
        /// sms or push.
        /// </summary>
        public string Medium { get; set; }

        public string Occasion { get; set; }

        public int DaysBefore { get; set; }

        /// <summary>
        /// The hour it goes out, 0 to 23.
        /// </summary>
        public int Hour { get; set; }

        public string Audience { get; set; }

        /// <summary>
        /// The phone number a text goes out on: main or students.
        /// </summary>
        public string Line { get; set; }

        public string Topic { get; set; }

        public string Title { get; set; }

        public string Text { get; set; }

        public bool AsksReply { get; set; }
    }

    /// <summary>
    /// content/communications/conversations.json: the pools generated text conversations are built from.
    /// </summary>
    internal sealed class ConversationLibrary
    {
        public List<ConversationTopic> Topics { get; set; } = new List<ConversationTopic>();
    }

    internal sealed class ConversationTopic
    {
        public string Key { get; set; }

        /// <summary>
        /// Where the topic happens: personal, main, or students.
        /// </summary>
        public List<string> Numbers { get; set; } = new List<string>();

        /// <summary>
        /// adult, parent, or student.
        /// </summary>
        public string Audience { get; set; }

        public List<string> Openers { get; set; } = new List<string>();

        public List<string> StaffReplies { get; set; } = new List<string>();

        public List<string> PersonFollowUps { get; set; } = new List<string>();

        public List<string> StaffFollowUps { get; set; } = new List<string>();

        public List<string> Closings { get; set; } = new List<string>();
    }

    /// <summary>
    /// cast/sms-threads.json: the Cast's hand-written text conversations.
    /// </summary>
    internal sealed class CastThreadFile
    {
        public List<CastThread> Threads { get; set; } = new List<CastThread>();
    }

    internal sealed class CastThread
    {
        public string Key { get; set; }

        public Guid Person { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The key of the phone number in configuration/communications.json.
        /// </summary>
        public string Number { get; set; }

        /// <summary>
        /// unread, read, or stopped.
        /// </summary>
        public string State { get; set; }

        public List<CastThreadMessage> Messages { get; set; } = new List<CastThreadMessage>();
    }

    internal sealed class CastThreadMessage
    {
        /// <summary>
        /// person or staff.
        /// </summary>
        public string From { get; set; }

        public int DaysAgo { get; set; }

        /// <summary>
        /// HH:mm, 24-hour.
        /// </summary>
        public string Time { get; set; }

        public string Text { get; set; }
    }
}
