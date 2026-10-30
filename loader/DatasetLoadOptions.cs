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
        /// The date every relative date counts back from. Null uses today on the database server
        /// (SQL Server's GETDATE), the date Rock Cleanup uses for ages and daily counts. Tests pin it
        /// so a load is the same every time.
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
        /// The parts to load. Null loads every part. The load adds the parts the chosen ones need
        /// (see <see cref="SampleDataParts.WithDependencies"/>). Church setup and people always load.
        /// </summary>
        public ICollection<SampleDataPart> Parts { get; set; }

        /// <summary>
        /// Receives each line of the load's progress. Null reports nothing. Reports are made on the
        /// loading thread as the load goes; <see cref="Progress{T}"/> posts them to its own context.
        /// </summary>
        public IProgress<LoadProgressReport> Progress { get; set; }

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
        /// A Rock web.config whose PasswordKey and DataEncryptionKey key the check scanner's matching
        /// fields and note mentions. Null, with no <see cref="PasswordKey"/>, leaves those fields empty
        /// and mentions as plain names; everything else still loads.
        /// </summary>
        public string RockWebConfigPath { get; set; }

        /// <summary>
        /// Rock's PasswordKey, in hex as web.config holds it. When set, it is used instead of the one in
        /// <see cref="RockWebConfigPath"/>. Rock passes its own, so check scanning finds the sample givers.
        /// </summary>
        public string PasswordKey { get; set; }

        /// <summary>
        /// Rock's DataEncryptionKey, as web.config holds it. When set, it is used instead of the one in
        /// <see cref="RockWebConfigPath"/>. It encrypts the check bank lines and keys note mentions.
        /// </summary>
        public string DataEncryptionKey { get; set; }

        /// <summary>
        /// The BCrypt hash of the password every sample login uses, made the way Rock's Database login stores it
        /// (decision 180). Rock passes its own Database authentication component's EncodePassword of the
        /// password the person installing typed, so the loader never sees the password. Null loads no logins.
        /// No password or hash is ever in a package.
        /// </summary>
        public string LoginPasswordHash { get; set; }

        /// <summary>
        /// True rolls everything back at the end, which is handy for testing.
        /// </summary>
        public bool IsDryRun { get; set; }

        /// <summary>
        /// The Rock the database belongs to, for example "20.0.10", from Rock's Rock.Version assembly.
        /// Rock passes VersionInfo.GetRockSemanticVersionNumber(); outside Rock, read it with
        /// <see cref="RockVersionInfo.Read"/>. Null reads it from the Rock.Version assembly this process
        /// runs, which only works inside Rock. A package loads only on its own Rock major release.
        /// </summary>
        public string RockVersion { get; set; }

        /// <summary>
        /// True loads even when the database's Rock is another major release than the package's, or is
        /// unknown, with a warning. For development databases. Rock's own installer leaves this false.
        /// </summary>
        public bool IsAnyRockVersionAllowed { get; set; }

        /// <summary>
        /// True checks the Cast expectations at the end of the load and reports each result: whether named Cast
        /// people show what their stories say, for those who build and test the sample data (decision 217).
        /// The command line and the verify tool turn it on. Rock's installer leaves it false, so a person installing
        /// sees no internal checks; a load with it false skips the step without a word.
        /// </summary>
        public bool IsCastExpectationsChecked { get; set; }

        /// <summary>
        /// When set, records what the load writes in Rock. Only the verify tool sets it.
        /// </summary>
        internal Database.WriteRecorder Recorder { get; set; }
    }
}
