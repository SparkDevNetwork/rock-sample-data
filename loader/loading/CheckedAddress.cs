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

using System;

using Microsoft.Data.SqlClient;

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        Rock ships with its Smarty Streets address service active, on Spark's managed key, and its Location Services
        Verify job runs every night at 3 AM (Rock/Jobs/LocationServicesVerify.cs). The job sends up to 1,000 addresses a
        night that are active, have a Street1, are not locked, and lack a standardized or geocoded date. A load wrote
        about 17,000 such addresses in Full 7, so every install sent them out for about 17 nights.

        So every Location the load writes with a street address or a map point carries the marks Rock's
        LocationService.Verify leaves after its service standardized and geocoded the address: both attempted dates, both
        success dates, the service's name, and its result. The job then finds no work, and Verify( location, false ),
        which Rock's REST endpoint and Person Address Update workflow action call, skips every service. The dates are
        the row's own created time, as when Rock checks an address the moment a person enters it. The result is the
        one Smarty Streets writes for a street match (DPV match code Y, at ZIP+4 precision). Rooms and buildings have
        no address and no map point, so Rock never checks them and they stay unmarked. Rock 20.0.10 and Rock 21.0.2
        select and mark addresses the same way (decision D-addresses-1).

        Reason: Jon's rule: sample addresses never go to an address service.
    */

    /// <summary>
    /// The marks Rock's LocationService.Verify leaves on a Location after its address service standardized and
    /// geocoded the address, so Rock's Location Services Verify job never sends it.
    /// </summary>
    internal static class CheckedAddress
    {
        /// <summary>
        /// The component name of Rock's Smarty Streets address service, the one Rock ships active.
        /// </summary>
        public const string ServiceType = "Smarty Streets";

        /// <summary>
        /// The result Smarty Streets writes for a street address it matched and placed: a street record, DPV match
        /// code Y, and ZIP+4 precision.
        /// </summary>
        public const string Result = "RecordType:S; DPV MatchCode:Y; Precision:Zip9";

        /// <summary>
        /// The Location columns the marks fill, for a bulk copy table, in the order of <see cref="Values"/>.
        /// </summary>
        public static readonly string[] Columns =
        {
            "StandardizeAttemptedDateTime:date",
            "StandardizeAttemptedServiceType:string",
            "StandardizeAttemptedResult:string",
            "StandardizedDateTime:date",
            "GeocodeAttemptedDateTime:date",
            "GeocodeAttemptedServiceType:string",
            "GeocodeAttemptedResult:string",
            "GeocodedDateTime:date"
        };

        /// <summary>
        /// The SET list of an UPDATE that marks a Location checked at <c>@CheckedAt</c>.
        /// </summary>
        public const string SetSql = @"[StandardizeAttemptedDateTime] = @CheckedAt, [StandardizeAttemptedServiceType] = @CheckedService, [StandardizeAttemptedResult] = @CheckedResult, [StandardizedDateTime] = @CheckedAt,
    [GeocodeAttemptedDateTime] = @CheckedAt, [GeocodeAttemptedServiceType] = @CheckedService, [GeocodeAttemptedResult] = @CheckedResult, [GeocodedDateTime] = @CheckedAt";

        /// <summary>
        /// The marks of an address checked at the given time, in the order of <see cref="Columns"/>.
        /// </summary>
        public static object[] Values( DateTime checkedAt ) => new object[] { checkedAt, ServiceType, Result, checkedAt, checkedAt, ServiceType, Result, checkedAt };

        /// <summary>
        /// The parameters <see cref="SetSql"/> reads.
        /// </summary>
        public static SqlParameter[] Parameters( DateTime checkedAt ) => new[]
        {
            new SqlParameter( "@CheckedAt", checkedAt ),
            new SqlParameter( "@CheckedService", ServiceType ),
            new SqlParameter( "@CheckedResult", Result )
        };
    }
}
