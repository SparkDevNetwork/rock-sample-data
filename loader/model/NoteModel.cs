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
        These classes mirror configuration/notes.json, the Cast note files in cast/notes/,
        and the text pools in content/notes/. Property names match the JSON keys (case-insensitive).
    */

    /// <summary>
    /// The contents of configuration/notes.json: the note types on person records, their security,
    /// and the staff who write notes.
    /// </summary>
    internal sealed class NotesConfiguration
    {
        /// <summary>
        /// Security role group Guids by the key the note types use in viewers and editors.
        /// </summary>
        public Dictionary<string, Guid> StaffRoles { get; set; } = new Dictionary<string, Guid>();

        public List<NoteStaff> Staff { get; set; } = new List<NoteStaff>();

        public CampusPastorRules CampusPastor { get; set; } = new CampusPastorRules();

        public List<NoteTypeDefinition> Types { get; set; } = new List<NoteTypeDefinition>();
    }

    /// <summary>
    /// A Cast staff member who writes notes.
    /// </summary>
    internal sealed class NoteStaff
    {
        public string Key { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The staff member's Cast person Guid.
        /// </summary>
        public Guid Guid { get; set; }

        public string Role { get; set; }
    }

    /// <summary>
    /// Who writes notes on campuses other than the main one.
    /// </summary>
    internal sealed class CampusPastorRules
    {
        /// <summary>
        /// Staff role keys each campus pastor joins, so they can view the notes they write.
        /// </summary>
        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>
        /// Note type keys the church office writes for every campus instead of the campus pastor.
        /// </summary>
        public List<string> OfficeTypes { get; set; } = new List<string>();

        /// <summary>
        /// The staff key of the office author.
        /// </summary>
        public string OfficeAuthor { get; set; }
    }

    /// <summary>
    /// One note type on person records: one of Rock's own, updated in place, or a new one.
    /// </summary>
    internal sealed class NoteTypeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// True for a type Rock ships, which the loader updates instead of inserting.
        /// </summary>
        public bool RockType { get; set; }

        public string IconCssClass { get; set; }

        public int Order { get; set; }

        /// <summary>
        /// True when every note of this type is an alert.
        /// </summary>
        public bool IsAlwaysAlert { get; set; }

        /// <summary>
        /// True when the type's notes never mention other staff and only the type's own authors reply,
        /// because only a few roles may view them.
        /// </summary>
        public bool IsConfidential { get; set; }

        /// <summary>
        /// Staff role keys allowed to view the type's notes. Everyone else is denied.
        /// </summary>
        public List<string> Viewers { get; set; } = new List<string>();

        /// <summary>
        /// Staff role keys allowed to edit the type's notes. Everyone else is denied.
        /// </summary>
        public List<string> Editors { get; set; } = new List<string>();

        /// <summary>
        /// The type's share of generated notes, in percent, by audience (guest, attender, child).
        /// </summary>
        public Dictionary<string, double> Weights { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Who writes the type's generated notes on the main campus, by staff key, with weights.
        /// </summary>
        public Dictionary<string, double> Authors { get; set; } = new Dictionary<string, double>();
    }

    /// <summary>
    /// One file in cast/notes: the hand-written notes on one Cast household's people.
    /// </summary>
    internal sealed class CastNoteFile
    {
        public string Household { get; set; }

        public List<CastNote> Notes { get; set; } = new List<CastNote>();
    }

    /// <summary>
    /// A hand-written note on a Cast person.
    /// </summary>
    internal sealed class CastNote
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The member key of the person the note is about.
        /// </summary>
        public string About { get; set; }

        /// <summary>
        /// A note type key from configuration/notes.json.
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// A staff key from configuration/notes.json.
        /// </summary>
        public string Author { get; set; }

        public RelativeDate Written { get; set; }

        /// <summary>
        /// The time of day, HH:mm.
        /// </summary>
        public string Time { get; set; }

        /// <summary>
        /// The note text. {@staffkey} becomes a mention of that staff member.
        /// </summary>
        public string Text { get; set; }

        public bool IsAlert { get; set; }

        public bool IsPrivate { get; set; }

        public bool IsPinned { get; set; }

        public List<CastNoteReply> Replies { get; set; } = new List<CastNoteReply>();
    }

    /// <summary>
    /// A staff reply to a Cast note.
    /// </summary>
    internal sealed class CastNoteReply
    {
        public Guid Guid { get; set; }

        public string Author { get; set; }

        public RelativeDate Written { get; set; }

        public string Time { get; set; }

        public string Text { get; set; }
    }

    /// <summary>
    /// The text pool for one audience of one note type.
    /// </summary>
    internal sealed class NotePoolSection
    {
        public List<string> Openers { get; set; } = new List<string>();

        public List<string> Details { get; set; } = new List<string>();

        /// <summary>
        /// Short heads-up notes marked as alerts. Only some types have them.
        /// </summary>
        public List<string> Alerts { get; set; } = new List<string>();
    }

    /// <summary>
    /// The contents of content/notes/shared.json: staff replies and mention lines for any type.
    /// </summary>
    internal sealed class SharedNotePool
    {
        public List<string> Replies { get; set; } = new List<string>();

        public List<string> MentionLines { get; set; } = new List<string>();
    }
}
