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
    /// configuration/peer-network.json: the group types with Rock's Peer Network on, and who follows whom
    /// (decisions 328 to 331).
    /// </summary>
    internal sealed class PeerNetworkConfiguration
    {
        /// <summary>
        /// The group types whose members Rock's job connects.
        /// </summary>
        public List<PeerNetworkGroupType> GroupTypes { get; set; } = new List<PeerNetworkGroupType>();

        /// <summary>
        /// Who follows whom, or null for no follows.
        /// </summary>
        public PeerNetworkFollows Follows { get; set; }
    }

    internal sealed class PeerNetworkGroupType
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// Rock's RelationshipStrength: casual, close, or deep.
        /// </summary>
        public string RelationshipStrength { get; set; }

        /// <summary>
        /// Whether a score grows each month while both people stay in the group.
        /// </summary>
        public bool IsGrowthEnabled { get; set; }

        /// <summary>
        /// Rock's points for the strength (Rock.Enums.Group.RelationshipStrength).
        /// </summary>
        public int Points => RelationshipStrength switch
        {
            "casual" => 5,
            "close" => 10,
            "deep" => 20,
            _ => throw new InvalidDataException( $"configuration/peer-network.json: {Name} has relationshipStrength \"{RelationshipStrength}\", which is not casual, close, or deep." )
        };
    }

    internal sealed class PeerNetworkFollows
    {
        /// <summary>
        /// Each campus pastor follows the leaders of their campus's active small groups.
        /// </summary>
        public bool CampusPastorsFollowSmallGroupLeaders { get; set; }

        /// <summary>
        /// Note type keys from configuration/notes.json whose authors follow the people the notes are about.
        /// </summary>
        public List<string> CareNoteTypes { get; set; } = new List<string>();

        /// <summary>
        /// How many months back from install day a care note counts.
        /// </summary>
        public int CareMonths { get; set; }

        /// <summary>
        /// The most people one staff member follows for care, those with the newest notes first.
        /// </summary>
        public int CareLimit { get; set; }
    }
}
