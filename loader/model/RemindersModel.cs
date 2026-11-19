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
    /// configuration/reminders.json: the reminders staff and leaders set for themselves (issue 22).
    /// </summary>
    internal sealed class RemindersConfiguration
    {
        /// <summary>
        /// Rock's shipped Personal Reminder type, which points at a person's primary alias.
        /// </summary>
        public Guid PersonReminderType { get; set; }

        /// <summary>
        /// Rock's shipped Group Reminder type, which points at a group.
        /// </summary>
        public Guid GroupReminderType { get; set; }

        public List<CastReminder> Cast { get; set; } = new List<CastReminder>();

        public GeneratedReminders Generated { get; set; } = new GeneratedReminders();

        public ReminderNotes Notes { get; set; } = new ReminderNotes();
    }

    /// <summary>
    /// One hand-written Cast reminder.
    /// </summary>
    internal sealed class CastReminder
    {
        public string Key { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The person who set the reminder and sees it.
        /// </summary>
        public Guid Owner { get; set; }

        public string OwnerName { get; set; }

        /// <summary>
        /// The person a Personal Reminder is about, or null for a Group Reminder.
        /// </summary>
        public Guid? Person { get; set; }

        /// <summary>
        /// The group a Group Reminder is about, or null for a Personal Reminder.
        /// </summary>
        public Guid? Group { get; set; }

        public string AboutName { get; set; }

        public string Note { get; set; }

        /// <summary>
        /// When the owner set it.
        /// </summary>
        public ReminderDay Set { get; set; }

        /// <summary>
        /// When it is due.
        /// </summary>
        public ReminderDay Date { get; set; }

        /// <summary>
        /// How it repeats, or null when it does not.
        /// </summary>
        public CastReminderRepeat Repeat { get; set; }

        /// <summary>
        /// What Rock shows on any install day: due, upcoming, or complete.
        /// </summary>
        public string Expect { get; set; }
    }

    /// <summary>
    /// A day that counts from the install day.
    /// </summary>
    internal sealed class ReminderDay
    {
        public int? DaysAgo { get; set; }

        public int? DaysAhead { get; set; }

        /// <summary>
        /// That many days before the next baptism Sunday after the install day, and never before tomorrow.
        /// </summary>
        public int? BeforeNextBaptismSunday { get; set; }
    }

    /// <summary>
    /// A Cast reminder that repeats: every so many days after it is completed, and how many times it has been.
    /// </summary>
    internal sealed class CastReminderRepeat
    {
        public int EveryDays { get; set; }

        public int Completed { get; set; }

        /// <summary>
        /// How many times it repeats in all, or null for every time.
        /// </summary>
        public int? Times { get; set; }
    }

    /// <summary>
    /// The rules for generated reminders.
    /// </summary>
    internal sealed class GeneratedReminders
    {
        public GeneratedPersonReminders Person { get; set; } = new GeneratedPersonReminders();

        public GeneratedGroupReminders Group { get; set; } = new GeneratedGroupReminders();

        /// <summary>
        /// How many times a repeating reminder repeats in all, drawn evenly. Null is every time.
        /// </summary>
        public List<int?> RepeatTimes { get; set; } = new List<int?>();

        /// <summary>
        /// The share of past reminders due in the last <see cref="DueWithinDays"/> days that are not complete yet.
        /// </summary>
        public double DuePercent { get; set; }

        public int DueWithinDays { get; set; }

        /// <summary>
        /// How many days after a reminder comes due its owner completes it.
        /// </summary>
        public DayRange CompletedDaysLate { get; set; } = new DayRange();
    }

    internal sealed class GeneratedPersonReminders
    {
        /// <summary>
        /// The share of each note type's notes, by key in configuration/notes.json, whose author sets a reminder.
        /// </summary>
        public Dictionary<string, double> PercentOfNotesByType { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Only notes written in this many days before the install day get a reminder.
        /// </summary>
        public int NoteWithinDays { get; set; }

        public DayRange DaysAfter { get; set; } = new DayRange();

        public double RepeatPercent { get; set; }

        public List<int> RepeatEveryDays { get; set; } = new List<int>();
    }

    internal sealed class GeneratedGroupReminders
    {
        public double PercentOfGroups { get; set; }

        /// <summary>
        /// A leader sets their reminder on a day in this many days before the install day.
        /// </summary>
        public int SetWithinDays { get; set; }

        public DayRange DaysAfter { get; set; } = new DayRange();

        public double RepeatPercent { get; set; }

        public List<int> RepeatEveryDays { get; set; } = new List<int>();
    }

    internal sealed class DayRange
    {
        public int Min { get; set; }

        public int Max { get; set; }
    }

    /// <summary>
    /// The notes generated reminders draw from.
    /// </summary>
    internal sealed class ReminderNotes
    {
        public List<string> Person { get; set; } = new List<string>();

        public List<string> Group { get; set; } = new List<string>();
    }
}
