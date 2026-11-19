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
    /// The contents of cast/duplicates.json: the Cast's duplicate records and what staff did about them.
    /// </summary>
    internal sealed class CastDuplicatesFile
    {
        public List<CastDuplicatePair> Pairs { get; set; } = new List<CastDuplicatePair>();

        public List<CastMergeRequest> MergeRequests { get; set; } = new List<CastMergeRequest>();

        public List<CastMerge> Merges { get; set; } = new List<CastMerge>();
    }

    /// <summary>
    /// A Cast person named in cast/duplicates.json.
    /// </summary>
    internal sealed class CastPersonReference
    {
        public Guid Guid { get; set; }

        public string Name { get; set; }
    }

    /// <summary>
    /// What a staff member decided about a pair Rock's duplicate finder scores.
    /// </summary>
    internal enum DuplicateDecision
    {
        /// <summary>
        /// Nobody has decided, so the Duplicate Finder lists the pair.
        /// </summary>
        Open,

        /// <summary>
        /// A staff member clicked Not Duplicate.
        /// </summary>
        NotADuplicate,

        /// <summary>
        /// A staff member clicked Ignore, which hides the pair until its score changes.
        /// </summary>
        Ignored
    }

    /// <summary>
    /// A pair of Cast people Rock's duplicate finder scores, and its decision.
    /// </summary>
    internal sealed class CastDuplicatePair
    {
        public string Key { get; set; }

        public List<CastPersonReference> People { get; set; } = new List<CastPersonReference>();

        /// <summary>
        /// "open", "not-a-duplicate", or "ignored", as the file writes it. See <see cref="DecisionValue"/>.
        /// </summary>
        public string Decision { get; set; }

        public CastPersonReference DecidedBy { get; set; }

        public RelativeDate Decided { get; set; }

        public string Story { get; set; }

        public DuplicateDecision DecisionValue => Decision switch
        {
            "open" => DuplicateDecision.Open,
            "not-a-duplicate" => DuplicateDecision.NotADuplicate,
            "ignored" => DuplicateDecision.Ignored,
            _ => throw new InvalidDataException( $"cast/duplicates.json: pair {Key} has decision \"{Decision}\", not open, not-a-duplicate, or ignored." )
        };
    }

    /// <summary>
    /// A merge asked for by someone who cannot merge people: Rock's Person Merge Request entity set.
    /// </summary>
    internal sealed class CastMergeRequest
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        public CastPersonReference Requester { get; set; }

        public List<CastPersonReference> People { get; set; } = new List<CastPersonReference>();

        public string Note { get; set; }

        public RelativeDate Requested { get; set; }

        public bool NotifyAfterMerge { get; set; }
    }

    /// <summary>
    /// A merge staff already made: a record Rock merged into a Cast person and deleted.
    /// </summary>
    internal sealed class CastMerge
    {
        public string Key { get; set; }

        /// <summary>
        /// The Cast person Rock kept.
        /// </summary>
        public CastPersonReference Person { get; set; }

        public CastMergedRecord Record { get; set; }

        public CastPersonReference MergedBy { get; set; }

        public RelativeDate Merged { get; set; }
    }

    /// <summary>
    /// The record Rock merged away. Its Guid lives on as the AliasPersonGuid of the alias Rock moved to the person.
    /// </summary>
    internal sealed class CastMergedRecord
    {
        public Guid Guid { get; set; }

        public string FirstName { get; set; }

        public string NickName { get; set; }

        public string LastName { get; set; }

        /// <summary>
        /// The record's full name as Rock wrote it in the Merged History line: the nick name, or else the first name, and the last name.
        /// </summary>
        public string FullName => $"{NickName ?? FirstName} {LastName}";
    }
}
