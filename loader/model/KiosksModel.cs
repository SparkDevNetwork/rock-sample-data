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
    /// configuration/kiosks.json: the check-in kiosks and label printers at each campus (decision D-checkin-attendance-1).
    /// </summary>
    internal sealed class KiosksConfiguration
    {
        /// <summary>
        /// The block every kiosk and printer address comes from, such as "192.0.2.0/24".
        /// </summary>
        public string Network { get; set; }

        /// <summary>
        /// Rock's shipped kiosk and printer, which become the first kids kiosk at one campus and its printer.
        /// </summary>
        public RockKiosk Rock { get; set; }

        /// <summary>
        /// The kinds of kiosk every campus has: kids, students, and volunteers.
        /// </summary>
        public List<KioskKind> Kinds { get; set; } = new List<KioskKind>();

        /// <summary>
        /// Each campus's number of kids kiosks and its first address.
        /// </summary>
        public List<CampusKiosks> Campuses { get; set; } = new List<CampusKiosks>();
    }

    /// <summary>
    /// The kiosk and printer Rock ships, and the campus they move to.
    /// </summary>
    internal sealed class RockKiosk
    {
        public string Campus { get; set; }

        public Guid KioskGuid { get; set; }

        public Guid PrinterGuid { get; set; }
    }

    /// <summary>
    /// One kind of kiosk. Names hold {campus} and, for kids kiosks, {number}.
    /// </summary>
    internal sealed class KioskKind
    {
        public string Key { get; set; }

        public string Name { get; set; }

        public string PrinterName { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The check-in group keys whose rooms the kiosk offers.
        /// </summary>
        public List<string> Rooms { get; set; } = new List<string>();

        /// <summary>
        /// True when the kiosk offers every room in the campus's building.
        /// </summary>
        public bool Building { get; set; }
    }

    /// <summary>
    /// How many kids kiosks a campus has, and the last part of its first address.
    /// </summary>
    internal sealed class CampusKiosks
    {
        public string Campus { get; set; }

        public int KidsKiosks { get; set; }

        public int FirstAddress { get; set; }
    }
}
