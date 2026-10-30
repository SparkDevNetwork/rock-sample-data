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
    /// configuration/reporting.json: the church's layered data views (docs/reporting.md), the schedules that persist
    /// them, and who should be able to see them.
    /// </summary>
    internal sealed class ReportingConfiguration
    {
        public List<NamedPersistSchedule> NamedSchedules { get; set; } = new List<NamedPersistSchedule>();

        public List<DataViewDefinition> DataViews { get; set; } = new List<DataViewDefinition>();

        /// <summary>
        /// The security walkthrough as expectations: what Rock's own security check must answer for a Cast person.
        /// Verify asks Rock each one.
        /// </summary>
        public List<AccessExpectation> Access { get; set; } = new List<AccessExpectation>();
    }

    /// <summary>
    /// A named schedule in Rock's Persisted DataView Schedules category, which a persisted data view can pick.
    /// </summary>
    internal sealed class NamedPersistSchedule
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public Guid Guid { get; set; }

        /// <summary>
        /// The time of day it runs, every day, such as "02:00".
        /// </summary>
        public string Time { get; set; }
    }

    /// <summary>
    /// One answer Rock's security check must give: whether a Cast person may take an action on a category or data view.
    /// </summary>
    internal sealed class AccessExpectation
    {
        /// <summary>
        /// The Cast person's Guid.
        /// </summary>
        public Guid Person { get; set; }

        /// <summary>
        /// The person's name, for messages.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A category's key in configuration/categories.json, or null when the item is a data view.
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// A data view's key, in any configuration file, or null when the item is a category.
        /// </summary>
        public string DataView { get; set; }

        /// <summary>
        /// Rock's action, such as View or Edit.
        /// </summary>
        public string Action { get; set; }

        public bool Allowed { get; set; }
    }
}
