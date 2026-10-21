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
        These classes mirror configuration/prayer.json and the text pools in content/prayer.
        Property names match the JSON keys (case-insensitive).
    */

    /// <summary>
    /// The contents of prayer.json: a weight for each of the prayer categories Rock ships.
    /// </summary>
    internal sealed class PrayerConfiguration
    {
        public PrayerCategoryDefinition RootCategory { get; set; }

        public List<PrayerCategoryDefinition> Categories { get; set; } = new List<PrayerCategoryDefinition>();
    }

    /// <summary>
    /// One prayer category Rock ships, found by its Guid.
    /// </summary>
    internal sealed class PrayerCategoryDefinition
    {
        /// <summary>
        /// Names the category's text pool, content/prayer/categories/{key}.json.
        /// </summary>
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The category's share of generated requests, in percent. 0 means none.
        /// </summary>
        public double Weight { get; set; }
    }

    /// <summary>
    /// The text pool for one prayer category.
    /// </summary>
    internal sealed class PrayerCategoryText
    {
        /// <summary>
        /// The main text of a request, written by the person asking.
        /// </summary>
        public List<string> Requests { get; set; } = new List<string>();

        /// <summary>
        /// Sentences that can follow any request in the category, to make longer requests.
        /// </summary>
        public List<string> Details { get; set; } = new List<string>();

        /// <summary>
        /// Updates from the requester that fit any request in the category, saved as the answer.
        /// </summary>
        public List<string> Answers { get; set; } = new List<string>();
    }

    /// <summary>
    /// The contents of content/prayer/comments.json.
    /// </summary>
    internal sealed class PrayerCommentText
    {
        /// <summary>
        /// Encouragements from prayer team members.
        /// </summary>
        public List<string> Team { get; set; } = new List<string>();

        /// <summary>
        /// Thanks or brief updates from the person who asked.
        /// </summary>
        public List<string> RequesterReplies { get; set; } = new List<string>();
    }

    /// <summary>
    /// The contents of content/prayer/shared.json.
    /// </summary>
    internal sealed class PrayerSharedText
    {
        /// <summary>
        /// Lines that can end any request.
        /// </summary>
        public List<string> Closings { get; set; } = new List<string>();
    }

    /// <summary>
    /// Every prayer text pool, keyed by category key.
    /// </summary>
    internal sealed class PrayerText
    {
        public Dictionary<string, PrayerCategoryText> Categories { get; } = new Dictionary<string, PrayerCategoryText>();

        public PrayerCommentText Comments { get; set; }

        public PrayerSharedText Shared { get; set; }
    }

    /// <summary>
    /// The contents of cast/prayer-requests.json: the Cast's own prayer requests.
    /// </summary>
    internal sealed class CastPrayerRequestFile
    {
        public List<CastPrayerRequest> Requests { get; set; } = new List<CastPrayerRequest>();
    }

    /// <summary>
    /// One Cast prayer request. Its state sets approval, expiration, flags, and the answer.
    /// </summary>
    internal sealed class CastPrayerRequest
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The Cast person who asked, or null for a guest.
        /// </summary>
        public Guid? Requester { get; set; }

        /// <summary>
        /// A guest with no person record: Rock keeps only the name and email they typed.
        /// </summary>
        public CastPrayerGuest Guest { get; set; }

        /// <summary>
        /// A category key from configuration/prayer.json.
        /// </summary>
        public string Category { get; set; }

        public RelativeDate Entered { get; set; }

        /// <summary>
        /// "current", "waiting", "flagged", "expired", or "answered".
        /// </summary>
        public string State { get; set; }

        public bool IsUrgent { get; set; }

        /// <summary>
        /// Private requests are not public (Rock's IsPublic is false).
        /// </summary>
        public bool IsPrivate { get; set; }

        public bool AllowComments { get; set; }

        /// <summary>
        /// The key of a Cast small group in cast/groups the request is shared with, or null.
        /// </summary>
        public string Group { get; set; }

        /// <summary>
        /// How many times the prayer team prayed for it.
        /// </summary>
        public int Prayers { get; set; }

        public string Text { get; set; }

        public string Answer { get; set; }

        public List<CastPrayerComment> Comments { get; set; } = new List<CastPrayerComment>();
    }

    /// <summary>
    /// A guest who asked for prayer without a person record.
    /// </summary>
    internal sealed class CastPrayerGuest
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }
    }

    /// <summary>
    /// One comment on a Cast prayer request.
    /// </summary>
    internal sealed class CastPrayerComment
    {
        /// <summary>
        /// "team" for a prayer team member who prayed for the request, or "requester" for a reply from the person who asked.
        /// </summary>
        public string By { get; set; }

        public string Text { get; set; }
    }
}
