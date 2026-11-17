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
    internal sealed partial class AdaptiveMessagesConfiguration
    {
        /// <summary>
        /// The External Website homepage block that shows the messages, or null to add no block.
        /// </summary>
        public AdaptiveMessageDisplay Display { get; set; }
    }

    /// <summary>
    /// The HTML block on a page that shows the church's messages through the adaptivemessage Lava command.
    /// </summary>
    internal sealed class AdaptiveMessageDisplay
    {
        /// <summary>
        /// The page's Guid.
        /// </summary>
        public Guid Page { get; set; }

        /// <summary>
        /// The layout zone, without spaces, as Rock stores it, such as SectionA.
        /// </summary>
        public string Zone { get; set; }

        public int Order { get; set; }

        public string Name { get; set; }

        public Guid BlockGuid { get; set; }

        public Guid ContentGuid { get; set; }

        /// <summary>
        /// The block's content: a Lava file in the package. {categoryId} becomes the messages' category Id.
        /// </summary>
        public string Content { get; set; }
    }
}
