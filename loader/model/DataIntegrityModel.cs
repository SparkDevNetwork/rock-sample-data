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

using System.Text.Json;

namespace RockSampleData.Loader.Model
{
    /// <summary>
    /// configuration/data-integrity.json: every designed problem on Rock's Data Integrity pages, and the Data Integrity
    /// Settings.
    /// </summary>
    internal sealed class DataIntegrityConfiguration
    {
        public RecordDatesRule RecordDates { get; set; } = new RecordDatesRule();

        public PendingRecords Pending { get; set; } = new PendingRecords();

        public SelfInactivations SelfInactivations { get; set; } = new SelfInactivations();

        public ConnectionStatusHistory ConnectionStatusHistory { get; set; } = new ConnectionStatusHistory();

        public DataErrorReports DataErrors { get; set; } = new DataErrorReports();

        public PhotoRequests PhotoRequests { get; set; } = new PhotoRequests();

        public SharedEmails SharedEmails { get; set; } = new SharedEmails();

        public FailedAddresses FailedAddresses { get; set; } = new FailedAddresses();

        public DataIntegritySettings Settings { get; set; } = new DataIntegritySettings();
    }

    /// <summary>
    /// When each Cast person's record was made.
    /// </summary>
    internal sealed class RecordDatesRule
    {
        /// <summary>
        /// The time of day a record made on a first visit carries, as HH:mm.
        /// </summary>
        public string Time { get; set; } = "11:20";

        /// <summary>
        /// How many days after birth a child born after the household's first visit was added.
        /// </summary>
        public int NewbornDays { get; set; } = 21;

        /// <summary>
        /// The day each household with no first visit got its records, by household key.
        /// </summary>
        public Dictionary<string, RelativeDate> Households { get; set; } = new Dictionary<string, RelativeDate>();
    }

    internal sealed class PendingRecords
    {
        public List<PendingRecord> Records { get; set; } = new List<PendingRecord>();
    }

    /// <summary>
    /// A Cast person whose record is Pending, and the form that made it.
    /// </summary>
    internal sealed class PendingRecord
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The Record Source value's name, or null for a record made before Rock recorded one.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// When the form made the record, or null to follow the record dates rule.
        /// </summary>
        public RelativeDate Created { get; set; }

        /// <summary>
        /// The connection status the form gave the record, which the status history starts from.
        /// </summary>
        public string ConnectionStatus { get; set; }

        public string Story { get; set; }
    }

    internal sealed class SelfInactivations
    {
        public List<SelfInactivation> Records { get; set; } = new List<SelfInactivation>();
    }

    /// <summary>
    /// A person who said they are no longer involved on Rock's Email Preferences page.
    /// </summary>
    internal sealed class SelfInactivation
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The Record Status Reason value's name.
        /// </summary>
        public string Reason { get; set; }

        public string Note { get; set; }

        public RelativeDate Inactivated { get; set; }

        public string Story { get; set; }
    }

    internal sealed class ConnectionStatusHistory
    {
        public List<StatusHistoryEntry> People { get; set; } = new List<StatusHistoryEntry>();
    }

    /// <summary>
    /// The same connection status changes, made for each listed person.
    /// </summary>
    internal sealed class StatusHistoryEntry
    {
        public List<NamedPerson> People { get; set; } = new List<NamedPerson>();

        public List<StatusChange> Changes { get; set; } = new List<StatusChange>();

        public string Story { get; set; }
    }

    internal sealed class NamedPerson
    {
        public Guid Person { get; set; }

        public string Name { get; set; }
    }

    internal sealed class StatusChange
    {
        public string From { get; set; }

        public string To { get; set; }

        public RelativeDate Changed { get; set; }

        /// <summary>
        /// The staff member who saved the change.
        /// </summary>
        public Guid By { get; set; }
    }

    internal sealed class DataErrorReports
    {
        public List<DataErrorReport> Reports { get; set; } = new List<DataErrorReport>();
    }

    /// <summary>
    /// A Person Data Error workflow a staff member started from a person's profile.
    /// </summary>
    internal sealed class DataErrorReport
    {
        public string Key { get; set; }

        public Guid Person { get; set; }

        public string Name { get; set; }

        public Guid ReportedBy { get; set; }

        public RelativeDate Reported { get; set; }

        public string Details { get; set; }

        /// <summary>
        /// When the assignee completed the report, or null while it waits.
        /// </summary>
        public RelativeDate Resolved { get; set; }

        public string Resolution { get; set; }

        /// <summary>
        /// The change the assignee made to the person, which Rock writes to their History, or null for none.
        /// </summary>
        public DataErrorFix Fix { get; set; }

        public string Story { get; set; }

        public bool IsResolved => Resolved != null;
    }

    internal sealed class DataErrorFix
    {
        public string ValueName { get; set; }

        public string OldValue { get; set; }

        public string NewValue { get; set; }
    }

    /// <summary>
    /// Photo Request workflows one staff member sent, and what each person did.
    /// </summary>
    internal sealed class PhotoRequests
    {
        public Guid Sender { get; set; }

        public string Message { get; set; }

        public List<PhotoRequest> Requests { get; set; } = new List<PhotoRequest>();
    }

    internal sealed class PhotoRequest
    {
        public Guid Person { get; set; }

        public string Name { get; set; }

        public RelativeDate Sent { get; set; }

        public RelativeDate Uploaded { get; set; }

        public RelativeDate Verified { get; set; }

        public RelativeDate OptedOut { get; set; }

        public string Story { get; set; }
    }

    internal sealed class SharedEmails
    {
        public List<SharedEmail> Households { get; set; } = new List<SharedEmail>();
    }

    internal sealed class SharedEmail
    {
        public string Household { get; set; }

        public string Email { get; set; }

        public string Story { get; set; }
    }

    /// <summary>
    /// Cast home addresses Rock's address service could not match, which a staff member locked.
    /// </summary>
    internal sealed class FailedAddresses
    {
        /// <summary>
        /// The result Rock's address service gave, which Rock saved as both attempt results.
        /// </summary>
        public string Result { get; set; }

        /// <summary>
        /// The staff member who checked Location Locked.
        /// </summary>
        public Guid LockedBy { get; set; }

        public List<FailedAddress> Households { get; set; } = new List<FailedAddress>();
    }

    internal sealed class FailedAddress
    {
        public string Household { get; set; }

        public string Story { get; set; }
    }

    /// <summary>
    /// Rock's Data Integrity Settings: each Data Automation rule's settings, in the shape of Rock's own settings class,
    /// keyed by its system setting.
    /// </summary>
    internal sealed class DataIntegritySettings
    {
        public Dictionary<string, JsonElement> Rules { get; set; } = new Dictionary<string, JsonElement>();
    }
}
