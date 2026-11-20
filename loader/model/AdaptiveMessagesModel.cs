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
    /// configuration/adaptive-messages.json: the church's personalization segments and adaptive messages (issue 6).
    /// </summary>
    internal sealed partial class AdaptiveMessagesConfiguration
    {
        /// <summary>
        /// The key of the adaptive message category in configuration/categories.json that holds every message.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// How often Rock's Update Personalization Data job refreshes each segment, in minutes. Rock's segment editor
        /// offers a daily interval by default.
        /// </summary>
        public int SegmentRefreshMinutes { get; set; } = 1440;

        /// <summary>
        /// External Website pages a text link may name as page:key.
        /// </summary>
        public Dictionary<string, AdaptiveMessagePage> Pages { get; set; } = new Dictionary<string, AdaptiveMessagePage>();

        public List<PersonalizationSegmentDefinition> Segments { get; set; } = new List<PersonalizationSegmentDefinition>();

        public List<AdaptiveMessageDefinition> Messages { get; set; } = new List<AdaptiveMessageDefinition>();

        /// <summary>
        /// Rock's segment Results page, which lists a segment's people without checking the segment's own security.
        /// </summary>
        public AdaptiveMessagePage ResultsPage { get; set; }

        /// <summary>
        /// What Rock's own security check must answer for a Cast person about a segment or a page. Verify asks Rock each one.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();

        /// <summary>
        /// Cast people and the adaptation each must see.
        /// </summary>
        public List<CastAdaptation> Cast { get; set; } = new List<CastAdaptation>();
    }

    /// <summary>
    /// A Rock page, by Guid.
    /// </summary>
    internal sealed class AdaptiveMessagePage
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// Rules on the page, written as Rock's security editor saves them.
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();
    }

    /// <summary>
    /// A personalization segment: the people in one person data view.
    /// </summary>
    internal sealed class PersonalizationSegmentDefinition
    {
        /// <summary>
        /// The segment's key, which is also its Segment Key in Rock.
        /// </summary>
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The key of its person data view, in any configuration file.
        /// </summary>
        public string DataView { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// Rules on this segment alone. A segment built on a Finance view is locked as tightly as Finance.
        /// </summary>
        public List<CategorySecurityRule> Security { get; set; } = new List<CategorySecurityRule>();
    }

    /// <summary>
    /// One adaptive message and its adaptations, in the order Rock tries them.
    /// </summary>
    internal sealed class AdaptiveMessageDefinition
    {
        /// <summary>
        /// The message's key, which is also its Key in Rock, the one the Lava command's messagekey names.
        /// </summary>
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// When the message is active, or null for always.
        /// </summary>
        public AdaptiveMessageDates Dates { get; set; }

        public List<AdaptationDefinition> Adaptations { get; set; } = new List<AdaptationDefinition>();
    }

    /// <summary>
    /// One version of a message, for the people in any of its segments, or for everyone when it has none.
    /// </summary>
    internal sealed class AdaptationDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// Segment keys. Rock shows the adaptation to a person in any of them.
        /// </summary>
        public List<string> Segments { get; set; } = new List<string>();

        public Guid Guid { get; set; }

        public AdaptiveMessageDates Dates { get; set; }

        public AdaptationSaturation Saturation { get; set; }
    }

    /// <summary>
    /// A rule for start and end dates, set at load from the install day.
    /// </summary>
    internal sealed class AdaptiveMessageDates
    {
        /// <summary>
        /// campaign, before-baptism-sunday, or camp-registration.
        /// </summary>
        public string Rule { get; set; }

        /// <summary>
        /// For before-baptism-sunday: how many days before the Sunday it starts.
        /// </summary>
        public int Days { get; set; }
    }

    /// <summary>
    /// Rock's View Saturation: the adaptation steps aside after this many views in this many days.
    /// </summary>
    internal sealed class AdaptationSaturation
    {
        public int Views { get; set; }

        public int Days { get; set; }
    }

    /// <summary>
    /// A Cast person and the adaptation Rock must show them for one message.
    /// </summary>
    internal sealed class CastAdaptation
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        public string Message { get; set; }

        /// <summary>
        /// The adaptation key, or null when the person must see no adaptation of the message.
        /// </summary>
        public string Adaptation { get; set; }

        public string Reason { get; set; }
    }

    /// <summary>
    /// content/adaptive-messages/messages.json: each adaptation's text, by message key and adaptation key.
    /// </summary>
    internal sealed class AdaptiveMessageText
    {
        public Dictionary<string, Dictionary<string, AdaptationText>> Messages { get; set; } = new Dictionary<string, Dictionary<string, AdaptationText>>();
    }

    /// <summary>
    /// The text Rock's adaptation attributes hold.
    /// </summary>
    internal sealed class AdaptationText
    {
        public string CallToAction { get; set; }

        /// <summary>
        /// An External Website route such as /Give, or page:key for a page in the configuration's pages.
        /// </summary>
        public string Link { get; set; }

        public string Summary { get; set; }

        public List<string> Details { get; set; } = new List<string>();

        /// <summary>
        /// The event key of an approved event image in photos/events/manifest.json, or null for none. An adaptation
        /// with no event image may have its own image in photos/messages/manifest.json instead.
        /// </summary>
        public string Image { get; set; }
    }

    /// <summary>
    /// The contents of photos/messages/manifest.json: the images made for adaptations that have no event image, from
    /// photos/message-brief.md. The image agent writes it, so it may be missing, and it may list only some adaptations.
    /// </summary>
    internal sealed class MessagePhotoManifest
    {
        public List<MessagePhotoEntry> Images { get; set; } = new List<MessagePhotoEntry>();
    }

    internal sealed class MessagePhotoEntry
    {
        /// <summary>
        /// The file under photos/, for example "messages/giving/first-gift.jpg".
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// The message's key in configuration/adaptive-messages.json.
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// The adaptation's key in that message.
        /// </summary>
        public string Adaptation { get; set; }
    }
}
