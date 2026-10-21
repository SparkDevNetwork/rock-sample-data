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
}
