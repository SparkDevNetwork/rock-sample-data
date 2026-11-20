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
    /// <summary>
    /// The contents of configuration/staff-homepage.json: Rock's staff homepage (issue 9).
    /// </summary>
    internal sealed class StaffHomepageConfiguration
    {
        public StaffUpdatesSettings Updates { get; set; }

        /// <summary>
        /// Rock's Internal Communication View block on the Internal Homepage.
        /// </summary>
        public Guid Block { get; set; }

        public List<HomepageMetric> Metrics { get; set; } = new List<HomepageMetric>();

        /// <summary>
        /// Adds the Ministry Path metrics from reporting.json to the block, after Rock's own.
        /// </summary>
        public bool PathMetrics { get; set; }

        public QuickLinks QuickLinks { get; set; }
    }

    internal sealed class StaffUpdatesSettings
    {
        /// <summary>
        /// Rock's Internal Communications - Homepage content channel.
        /// </summary>
        public Guid ContentChannel { get; set; }

        /// <summary>
        /// Rock's own 2018 item, which becomes the oldest update in place.
        /// </summary>
        public Guid RockItem { get; set; }

        /// <summary>
        /// The person who writes and posts every update.
        /// </summary>
        public Guid Author { get; set; }

        public int Weeks { get; set; } = 52;

        /// <summary>
        /// The time each Monday's update is posted, such as "08:00".
        /// </summary>
        public string PostTime { get; set; }

        /// <summary>
        /// The link every feature has.
        /// </summary>
        public string Link { get; set; }

        /// <summary>
        /// How many articles an update has, as weights by count.
        /// </summary>
        public Dictionary<string, int> ArticleCounts { get; set; } = new Dictionary<string, int>();
    }

    /// <summary>
    /// One of Rock's homepage metrics, with the schedule Rock's Calculate Metrics job runs it on.
    /// </summary>
    internal sealed class HomepageMetric
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public HomepageMetricSchedule Schedule { get; set; }

        /// <summary>
        /// The metric counts each campus on its own, through Rock's Campus partition.
        /// </summary>
        public bool ByCampus { get; set; }
    }

    internal sealed class HomepageMetricSchedule
    {
        /// <summary>
        /// The day of the week it runs, such as Monday, or null for every day.
        /// </summary>
        public string Day { get; set; }

        /// <summary>
        /// The time of day it runs, such as "01:00".
        /// </summary>
        public string Time { get; set; }
    }

    internal sealed class QuickLinks
    {
        /// <summary>
        /// The HtmlContent row of Rock's Quick Links block.
        /// </summary>
        public Guid Content { get; set; }

        public List<QuickLink> Links { get; set; } = new List<QuickLink>();
    }

    internal sealed class QuickLink
    {
        public string Label { get; set; }

        public string Href { get; set; }
    }

    /// <summary>
    /// The contents of content/internal-news/updates.json: the features and articles the weekly updates are made of.
    /// </summary>
    internal sealed class InternalNewsLibrary
    {
        public List<NewsFeature> Features { get; set; } = new List<NewsFeature>();

        public List<NewsFeature> EvergreenFeatures { get; set; } = new List<NewsFeature>();

        public List<NewsArticle> Articles { get; set; } = new List<NewsArticle>();

        public List<NewsArticle> EvergreenArticles { get; set; } = new List<NewsArticle>();
    }

    internal sealed class NewsFeature
    {
        public string Key { get; set; }

        /// <summary>
        /// An occasion the message library dates, or event:key for a calendar event. Null for an evergreen feature.
        /// </summary>
        public string Occasion { get; set; }

        /// <summary>
        /// How many weeks before its occasion's week it runs: 0 is the Monday of that week.
        /// </summary>
        public int WeeksBefore { get; set; }

        public string Title { get; set; }

        public List<string> Text { get; set; } = new List<string>();

        /// <summary>
        /// The event key of an approved event image.
        /// </summary>
        public string Image { get; set; }
    }

    internal sealed class NewsArticle
    {
        public string Key { get; set; }

        public string Occasion { get; set; }

        public int WeeksBefore { get; set; }

        /// <summary>
        /// The staff member who wrote it, as household/member. Their staff photo is its image.
        /// </summary>
        public string Author { get; set; }

        public string Title { get; set; }

        public string Text { get; set; }
    }
}
