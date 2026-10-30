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
        These classes mirror the JSON files in configuration/.
        Property names match the JSON keys (case-insensitive).
    */

    /// <summary>
    /// The contents of schedules.json: shared service time slots and campus schedule types.
    /// </summary>
    internal sealed class ScheduleConfiguration
    {
        public int CheckInStartOffsetMinutes { get; set; }

        public int CheckInEndOffsetMinutes { get; set; }

        public List<ScheduleTypeConfiguration> ScheduleTypes { get; set; } = new List<ScheduleTypeConfiguration>();

        public List<ScheduleSlot> Slots { get; set; } = new List<ScheduleSlot>();
    }

    /// <summary>
    /// A campus schedule type, such as Weekend Service or Student Service.
    /// </summary>
    internal sealed class ScheduleTypeConfiguration
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public bool RockDefault { get; set; }
    }

    /// <summary>
    /// One shared service time slot. Every campus offers a subset of these.
    /// </summary>
    internal sealed class ScheduleSlot
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public string Day { get; set; }

        public string Start { get; set; }

        public int Minutes { get; set; }

        public bool RockDefault { get; set; }
    }

    /// <summary>
    /// The contents of campuses.json.
    /// </summary>
    internal sealed class CampusConfiguration
    {
        public List<CampusDefinition> Campuses { get; set; } = new List<CampusDefinition>();
    }

    /// <summary>
    /// One campus, the datasets it appears in, and the slots it offers.
    /// </summary>
    internal sealed class CampusDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public bool RockDefault { get; set; }

        public string ShortCode { get; set; }

        public List<string> Datasets { get; set; } = new List<string>();

        public CampusLocation Location { get; set; }

        public List<CampusScheduleDefinition> Schedules { get; set; } = new List<CampusScheduleDefinition>();
    }

    /// <summary>
    /// A campus location. Coordinates are approximate until the address pool assigns a street address.
    /// </summary>
    internal sealed class CampusLocation
    {
        public Guid Guid { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }

    /// <summary>
    /// A slot a campus offers, and its campus schedule type.
    /// </summary>
    internal sealed class CampusScheduleDefinition
    {
        public string Slot { get; set; }

        public string Type { get; set; }
    }

    /// <summary>
    /// The contents of accounts.json.
    /// </summary>
    internal sealed class AccountConfiguration
    {
        public List<AccountDefinition> Accounts { get; set; } = new List<AccountDefinition>();
    }

    /// <summary>
    /// A top-level fund and its optional per-campus child accounts.
    /// </summary>
    internal sealed class AccountDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string PublicName { get; set; }

        public Guid Guid { get; set; }

        public bool RockDefault { get; set; }

        public bool IsTaxDeductible { get; set; }

        public int Order { get; set; }

        public List<CampusAccountDefinition> CampusAccounts { get; set; } = new List<CampusAccountDefinition>();
    }

    /// <summary>
    /// A fund's child account for one campus.
    /// </summary>
    internal sealed class CampusAccountDefinition
    {
        public string Campus { get; set; }

        public string Name { get; set; }

        public string PublicName { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// The contents of checkin.json.
    /// </summary>
    internal sealed class CheckInConfiguration
    {
        public List<CheckInArea> Areas { get; set; } = new List<CheckInArea>();

        public List<CheckInGroupDefinition> Groups { get; set; } = new List<CheckInGroupDefinition>();

        public string Building { get; set; }

        public Dictionary<string, int> RoomSoftThreshold { get; set; } = new Dictionary<string, int>();

        public string StudentFallbackSlot { get; set; }

        public WorshipConfiguration Worship { get; set; }
    }

    /// <summary>
    /// A check-in area (group type) and the keys of its groups.
    /// </summary>
    internal sealed class CheckInArea
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }

        public List<string> Groups { get; set; } = new List<string>();
    }

    /// <summary>
    /// A check-in group, its room name, and which services it meets at ("weekend" or "student").
    /// </summary>
    internal sealed class CheckInGroupDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string MatchBy { get; set; }

        public string Room { get; set; }

        public string Schedules { get; set; }
    }

    /// <summary>
    /// The adult worship attendance group.
    /// </summary>
    internal sealed class WorshipConfiguration
    {
        public NamedGuid Template { get; set; }

        public string GroupName { get; set; }
    }

    /// <summary>
    /// A name and Guid pair.
    /// </summary>
    internal sealed class NamedGuid
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }
    }

    /// <summary>
    /// The contents of connection-statuses.json.
    /// </summary>
    internal sealed class ConnectionStatusConfiguration
    {
        public List<ConnectionStatusDefinition> Statuses { get; set; } = new List<ConnectionStatusDefinition>();
    }

    /// <summary>
    /// One connection status. Rock system values are renamed; new ones are added.
    /// </summary>
    internal sealed class ConnectionStatusDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public bool RockDefault { get; set; }

        public int Order { get; set; }

        public string Description { get; set; }
    }

    /// <summary>
    /// A dataset manifest from datasets/{key}.json.
    /// </summary>
    internal sealed class DatasetManifest
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public DatasetCheckImages CheckImages { get; set; }

        public DatasetPhotos Photos { get; set; }

        public DatasetGeneratedHouseholds GeneratedHouseholds { get; set; }

        public DatasetGiving Giving { get; set; }

        // The dataset card. SampleDataDatasets reads these and requires every one.
        public string Summary { get; set; }

        public string BestFor { get; set; }

        public List<string> Highlights { get; set; }

        public int People { get; set; }

        public int Households { get; set; }

        public int Campuses { get; set; }

        public int TypicalInstallSeconds { get; set; }

        public int DatabaseSpaceMegabytes { get; set; }
    }

    /// <summary>
    /// Giving settings for one dataset.
    /// </summary>
    internal sealed class DatasetGiving
    {
        /// <summary>
        /// Multiplies the weight of a check when a gift's payment method is picked, for every giver in the dataset
        /// whose Cast file does not name a method. 1 (or not set) keeps the measured mix. A small dataset raises it
        /// so its check batches and check images have enough to show.
        /// </summary>
        public double? CheckWeightScale { get; set; }
    }

    /// <summary>
    /// Generated households the dataset adds beside the Cast. --crowd replaces these with households spread across every campus.
    /// </summary>
    internal sealed class DatasetGeneratedHouseholds
    {
        /// <summary>
        /// How many households to generate at each campus, keyed by campus key. A campus not listed gets none.
        /// </summary>
        public Dictionary<string, int> PerCampus { get; set; }

        /// <summary>
        /// Optional connection status weights for these households, keyed by status name (Prospect to Leading).
        /// A status's weight sets how many households start with it, and the status sets how likely the household
        /// is to attend and give. When not set, the generator's measured weights are used.
        /// </summary>
        public Dictionary<string, double> StatusWeights { get; set; }
    }

    /// <summary>
    /// Which generated households get pool photos. The Cast always gets its own photos.
    /// </summary>
    internal sealed class DatasetPhotos
    {
        /// <summary>
        /// Percent of generated households, per campus, that get photos from the pool. 0 means none.
        /// </summary>
        public int GeneratedHouseholdPercent { get; set; }
    }

    /// <summary>
    /// Which check gifts get scanned check images.
    /// </summary>
    internal sealed class DatasetCheckImages
    {
        /// <summary>
        /// Months of check gifts, counting back from the anchor, that get images. 0 means every check.
        /// </summary>
        public int Months { get; set; }
    }

    /// <summary>
    /// configuration/giving-alert-types.json: the sample church's giving alert types.
    /// </summary>
    internal sealed class GivingAlertTypeConfiguration
    {
        public List<GivingAlertTypeDefinition> AlertTypes { get; set; } = new List<GivingAlertTypeDefinition>();
    }

    /// <summary>
    /// One giving alert type. Fields left out stay empty in Rock, which means "no limit".
    /// </summary>
    internal sealed class GivingAlertTypeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// "gratitude" or "followUp".
        /// </summary>
        public string AlertType { get; set; }

        public int Order { get; set; }

        public bool ContinueIfMatched { get; set; }

        public int? RepeatPreventionDuration { get; set; }

        public decimal? FrequencySensitivityScale { get; set; }

        public decimal? AmountSensitivityScale { get; set; }

        public decimal? MinimumGiftAmount { get; set; }

        public decimal? MaximumGiftAmount { get; set; }

        public decimal? MinimumMedianGiftAmount { get; set; }

        public decimal? MaximumMedianGiftAmount { get; set; }

        public int? MaximumDaysSinceLastGift { get; set; }
    }

    /// <summary>
    /// configuration/achievements.json: the kids check-in streak type and the achievement types that read it.
    /// </summary>
    internal sealed class AchievementConfiguration
    {
        public AchievementStreakTypeDefinition StreakType { get; set; }

        /// <summary>
        /// In processing order: a prerequisite always comes before the types that need it.
        /// </summary>
        public List<AchievementTypeDefinition> AchievementTypes { get; set; } = new List<AchievementTypeDefinition>();
    }

    /// <summary>
    /// The weekly streak type that counts check-ins under one check-in template.
    /// </summary>
    internal sealed class AchievementStreakTypeDefinition
    {
        public string Name { get; set; }

        public Guid Guid { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The check-in template GroupType whose areas count. Its template setting also lists the achievement types.
        /// </summary>
        public Guid CheckInTemplateGuid { get; set; }
    }

    /// <summary>
    /// One achievement type on the streak type.
    /// </summary>
    internal sealed class AchievementTypeDefinition
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public Guid Guid { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// "consecutive" (Rock's Streaks: Consecutive) or "accumulative" (Streaks: Accumulative).
        /// </summary>
        public string Component { get; set; }

        /// <summary>
        /// Weeks in a row (consecutive) or weeks in all (accumulative) to succeed.
        /// </summary>
        public int Target { get; set; }

        /// <summary>
        /// How many times a person can succeed. Null means unlimited.
        /// </summary>
        public int? MaxAccomplishmentsAllowed { get; set; }

        /// <summary>
        /// The key of the achievement type a person must succeed at first, or null.
        /// </summary>
        public string Prerequisite { get; set; }

        public string IconCssClass { get; set; }

        public string HighlightColor { get; set; }

        public bool IsConsecutive => Component == "consecutive";
    }

    /// <summary>
    /// The contents of security-roles.json: how Cast role names map to the security roles Rock ships.
    /// </summary>
    internal sealed class SecurityRoleConfiguration
    {
        public List<SecurityRoleMapping> Roles { get; set; } = new List<SecurityRoleMapping>();

        /// <summary>
        /// Names Cast files list that are not Rock security roles. No one joins a role for them.
        /// </summary>
        public List<SecurityRoleMapping> NotRoles { get; set; } = new List<SecurityRoleMapping>();
    }

    /// <summary>
    /// One Cast role name and the Rock security role group it means.
    /// </summary>
    internal sealed class SecurityRoleMapping
    {
        public string Name { get; set; }

        public string RockRole { get; set; }

        public Guid Guid { get; set; }

        public string Reason { get; set; }
    }
}
