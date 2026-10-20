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
    /// photos/manifest.json: one entry per delivered photo.
    /// </summary>
    internal sealed class PhotoManifest
    {
        public List<PhotoEntry> Images { get; set; } = new List<PhotoEntry>();
    }

    /// <summary>
    /// One delivered photo. Cast photos name their household and member; pool photos carry the
    /// photo ethnicity, gender, and age band the builder matches generated people against.
    /// </summary>
    internal sealed class PhotoEntry
    {
        /// <summary>
        /// The file, relative to the photos folder (for example "cast/decker-ted.jpg").
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// "cast" or "core-3-pool".
        /// </summary>
        public string Set { get; set; }

        public string Household { get; set; }

        public string Member { get; set; }

        public string Ethnicity { get; set; }

        public string Gender { get; set; }

        public int Age { get; set; }

        public string AgeBand { get; set; }
    }

    /// <summary>
    /// photos/groups/manifest.json: one photo of each Cast small group's members.
    /// </summary>
    internal sealed class GroupPhotoManifest
    {
        public List<GroupPhotoEntry> Images { get; set; } = new List<GroupPhotoEntry>();
    }

    /// <summary>
    /// One delivered group photo.
    /// </summary>
    internal sealed class GroupPhotoEntry
    {
        /// <summary>
        /// The file, relative to the photos folder (for example "groups/decker-group.jpg").
        /// </summary>
        public string File { get; set; }

        /// <summary>
        /// The Cast small group's key, as in cast/groups.
        /// </summary>
        public string Group { get; set; }
    }
}
