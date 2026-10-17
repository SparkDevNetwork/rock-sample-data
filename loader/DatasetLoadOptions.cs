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

namespace RockSampleData.Loader
{
    /// <summary>
    /// How <see cref="DatasetLoader"/> loads a package. The defaults give a normal install.
    /// </summary>
    public sealed class DatasetLoadOptions
    {
        /// <summary>
        /// The date every relative date counts back from. Null uses today. Tests pin it so a load
        /// is the same every time.
        /// </summary>
        public DateTime? Anchor { get; set; }

        /// <summary>
        /// The text every random choice starts from. The same seed and anchor give the same data, row for row.
        /// </summary>
        public string Seed { get; set; } = "rock-sample-data";

        /// <summary>
        /// Years of history to generate before the anchor.
        /// </summary>
        public int HistoryYears { get; set; } = 3;

        /// <summary>
        /// When set, generates this many crowd households across the dataset's campuses instead of
        /// the counts in the dataset's manifest. For speed tests and validation.
        /// </summary>
        public int? CrowdHouseholds { get; set; }

        /// <summary>
        /// True draws scanned check images for check gifts. It is the slowest step, so it is off by default.
        /// </summary>
        public bool IsCheckImagesDrawn { get; set; }

        /// <summary>
        /// Months of check gifts, counting back from the anchor, that get images. Null uses the
        /// dataset's manifest (12 when it has none); 0 means every check.
        /// </summary>
        public int? CheckImageMonths { get; set; }

        /// <summary>
        /// When set, also saves each check image to this folder, for looking at them.
        /// </summary>
        public string CheckImageCopyFolder { get; set; }

        /// <summary>
        /// A Rock web.config whose PasswordKey fills the check scanner's matching fields. Null
        /// leaves those fields empty; everything else still loads.
        /// </summary>
        public string RockWebConfigPath { get; set; }

        /// <summary>
        /// True rolls everything back at the end, which is handy for testing.
        /// </summary>
        public bool IsDryRun { get; set; }

        /// <summary>
        /// True loads even when the database's newest migration is not the one the package was
        /// built for, with a warning. For development databases, where new migrations land often.
        /// Rock's own installer leaves this false.
        /// </summary>
        public bool IsAnyRockVersionAllowed { get; set; }
    }
}
