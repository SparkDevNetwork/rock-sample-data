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

using System.Data;
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        A refund is written the way Rock's Transaction Detail block saves one through
        FinancialTransactionService.ProcessRefund, which is the same in Rock 20.0.10 and 21.0.2:

        1. Rock refuses a refund of a refund, a refund of a gift in a foreign currency, and a refund of more than is
           left of the original after its earlier refunds. This writer throws instead of writing one.
        2. The refund is a new transaction with the original's type, person, gateway, and source. Through a gateway,
           the Test Gateway's Credit gives it a code of "T" and the time to the millisecond. Without one, as for a
           check, it has no code. Its payment detail copies the original's currency, card type, and masked number.
        3. The amount comes off the original's lines in order, each line up to its own amount, as negative lines on
           the same accounts and, for a registration payment, the same registration.
        4. A FinancialTransactionRefund row, which shares the refund's Id, holds the reason, the summary, and the
           original's Id.
        5. It lands in the day's batch FinancialBatchService finds or makes: an empty prefix, the card type's batch
           suffix or the currency, and the block's " - Refund" suffix, such as " VMD - Refund". Rock's name starts
           with a space, and the writer keeps it. The batch has no campus, and its control amount is its refunds' sum.
        6. The staff member who refunded it is the creator of every row, as Rock's save records the current person.

        Reason: Rock's transaction, batch, and statement screens read these rows as if staff had refunded the gift
        (decision D-refunds-1). Registration Follow-ups reuses the writer for a cancelled registration's refund.
    */

    /// <summary>
    /// One line of a transaction a refund can return, in the order Rock reads the lines (by Id).
    /// </summary>
    internal sealed record RefundableLine( int AccountId, decimal Amount, int? EntityTypeId = null, int? EntityId = null );

    /// <summary>
    /// A transaction as ProcessRefund reads it.
    /// </summary>
    /// <param name="IsRefund">True when the transaction is itself a refund, which Rock refuses to refund.</param>
    internal sealed record RefundableTransaction( int Id, int AuthorizedPersonAliasId, DateTime When, int TransactionTypeValueId, int? SourceTypeValueId,
        int? GatewayId, string TransactionCode, int? ForeignCurrencyCodeValueId, int CurrencyTypeValueId, int? CreditCardTypeValueId, string AccountNumberMasked,
        bool IsRefund, IReadOnlyList<RefundableLine> Lines )
    {
        public decimal TotalAmount => Lines.Sum( l => l.Amount );
    }

    /// <summary>
    /// One refund: what Rock's refund dialog asks for (the amount, reason, and summary), when it was made, and who made it.
    /// </summary>
    internal sealed record RefundRequest( RefundableTransaction Original, decimal Amount, int ReasonValueId, string Summary, DateTime When, int? RefundedByAliasId );

    /// <summary>
    /// Plans refund rows the way ProcessRefund saves them. <see cref="Tables"/> holds them in foreign key order, so
    /// the caller writes them after the originals and their batches.
    /// </summary>
    internal sealed class RefundWriter
    {
        /// <summary>
        /// The Transaction Detail block's default Refund Batch Name Suffix.
        /// </summary>
        public const string BatchNameSuffix = " - Refund";

        /// <summary>
        /// Rock's BatchStatus values. Staff close a refund batch within a week, as with the registration batches.
        /// </summary>
        private const int BatchOpen = 1;
        private const int BatchClosed = 2;
        private const int OpenBatchDays = 7;

        private readonly PaymentMethods _payments;
        private readonly string _foreignKey;
        private readonly DateTime _anchor;
        private int _nextBatchId;
        private int _nextPaymentDetailId;
        private int _nextTransactionId;
        private int _nextDetailId;

        private readonly DataTable _batches = TableFactory.Create( "Id:int", "Name:string", "BatchStartDateTime:date", "BatchEndDateTime:date", "Status:int", "ControlAmount:decimal", "IsAutomated:bool",
            "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );
        private readonly DataTable _paymentDetails = TableFactory.Create( "Id:int", "CurrencyTypeValueId:int", "CreditCardTypeValueId:int", "AccountNumberMasked:string",
            "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );
        private readonly DataTable _transactions = TableFactory.Create( "Id:int", "BatchId:int", "TransactionDateTime:date", "TransactionCode:string", "TransactionTypeValueId:int", "SourceTypeValueId:int",
            "AuthorizedPersonAliasId:int", "FinancialGatewayId:int", "FinancialPaymentDetailId:int", "ShowAsAnonymous:bool", "SundayDate:date", "TransactionDateKey:int",
            "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );
        private readonly DataTable _details = TableFactory.Create( "Id:int", "TransactionId:int", "AccountId:int", "Amount:decimal", "EntityTypeId:int", "EntityId:int",
            "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );
        private readonly DataTable _refunds = TableFactory.Create( "Id:int", "OriginalTransactionId:int", "RefundReasonValueId:int", "RefundReasonSummary:string",
            "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );

        private readonly Dictionary<(string Name, DateTime Day), DataRow> _batchByNameAndDay = new Dictionary<(string, DateTime), DataRow>();
        private readonly Dictionary<int, decimal> _refundedByOriginal = new Dictionary<int, decimal>();
        private readonly HashSet<int> _refundIds = new HashSet<int>();

        /// <param name="next">The next free Id in FinancialBatch, FinancialPaymentDetail, FinancialTransaction, and FinancialTransactionDetail.</param>
        public RefundWriter( PaymentMethods payments, string foreignKey, DateTime anchor, (int Batch, int PaymentDetail, int Transaction, int Detail) next )
        {
            _payments = payments;
            _foreignKey = foreignKey;
            _anchor = anchor;
            (_nextBatchId, _nextPaymentDetailId, _nextTransactionId, _nextDetailId) = next;
        }

        /// <summary>
        /// The planned rows, in foreign key order.
        /// </summary>
        public IReadOnlyList<(string Table, DataTable Rows)> Tables => new List<(string, DataTable)>
        {
            ("FinancialBatch", _batches),
            ("FinancialPaymentDetail", _paymentDetails),
            ("FinancialTransaction", _transactions),
            ("FinancialTransactionDetail", _details),
            ("FinancialTransactionRefund", _refunds)
        };

        public int Count => _refunds.Rows.Count;

        /// <summary>
        /// The refunds' total, a negative amount.
        /// </summary>
        public decimal Total => _details.AsEnumerable().Sum( r => ( decimal ) r["Amount"] );

        /// <summary>
        /// Plans one refund, and returns its lines. Throws when Rock would refuse it.
        /// </summary>
        public List<RefundableLine> Add( RefundRequest request )
        {
            var original = request.Original;
            if ( original.IsRefund || _refundIds.Contains( original.Id ) )
            {
                throw new InvalidOperationException( $"Transaction {original.Id} is a refund, and Rock does not refund a refund." );
            }

            // Rock refunds a transaction in the organization's own currency, which the sample data never names.
            if ( original.ForeignCurrencyCodeValueId.HasValue )
            {
                throw new InvalidOperationException( $"Transaction {original.Id} is in a foreign currency, and Rock does not refund one." );
            }

            var refundable = original.TotalAmount + _refundedByOriginal.GetValueOrDefault( original.Id );
            if ( request.Amount <= 0 || request.Amount > refundable )
            {
                throw new InvalidOperationException( $"A refund of {request.Amount:C} on transaction {original.Id} is outside what Rock allows (more than $0.00 and at most {refundable:C})." );
            }

            if ( request.When <= original.When )
            {
                throw new InvalidOperationException( $"The refund of transaction {original.Id} is dated before the transaction." );
            }

            var lines = Split( original.Lines, request.Amount );
            var refundTotal = lines.Sum( l => l.Amount );
            _refundedByOriginal[original.Id] = _refundedByOriginal.GetValueOrDefault( original.Id ) + refundTotal;

            var when = request.When;
            var by = request.RefundedByAliasId ?? ( object ) DBNull.Value;
            var batch = Batch( _payments.BatchSuffix( original.CurrencyTypeValueId, original.CreditCardTypeValueId ), when, by );
            batch["ControlAmount"] = ( decimal ) batch["ControlAmount"] + refundTotal;

            var paymentDetailId = _nextPaymentDetailId++;
            _paymentDetails.Rows.Add( paymentDetailId, original.CurrencyTypeValueId, original.CreditCardTypeValueId ?? ( object ) DBNull.Value, original.AccountNumberMasked ?? ( object ) DBNull.Value,
                Stable( $"payment-detail:{original.Id}:{when:o}" ), _foreignKey, when, when, by, by );

            // ProcessRefund credits the gateway only when the original went through one and has its code.
            var isThroughGateway = original.GatewayId.HasValue && !string.IsNullOrWhiteSpace( original.TransactionCode );
            var code = isThroughGateway ? "T" + when.ToString( "yyyyMMddHHmmssFFF", CultureInfo.InvariantCulture ) : null;

            var transactionId = _nextTransactionId++;
            _refundIds.Add( transactionId );
            _transactions.Rows.Add( transactionId, batch["Id"], when, code ?? ( object ) DBNull.Value, original.TransactionTypeValueId, original.SourceTypeValueId ?? ( object ) DBNull.Value,
                original.AuthorizedPersonAliasId, original.GatewayId ?? ( object ) DBNull.Value, paymentDetailId, false, SundayCalendar.SundayOnOrAfter( when.Date ),
                int.Parse( when.ToString( "yyyyMMdd", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture ),
                Stable( $"transaction:{original.Id}:{when:o}" ), _foreignKey, when, when, by, by );

            for ( var i = 0; i < lines.Count; i++ )
            {
                var line = lines[i];
                _details.Rows.Add( _nextDetailId++, transactionId, line.AccountId, line.Amount, line.EntityTypeId ?? ( object ) DBNull.Value, line.EntityId ?? ( object ) DBNull.Value,
                    Stable( $"detail:{original.Id}:{when:o}:{i}" ), _foreignKey, when, when, by, by );
            }

            _refunds.Rows.Add( transactionId, original.Id, request.ReasonValueId, request.Summary ?? ( object ) DBNull.Value,
                Stable( $"refund:{original.Id}:{when:o}" ), _foreignKey, when, when, by, by );

            return lines;
        }

        /// <summary>
        /// Port of ProcessRefund's split: the amount comes off each positive line in order, up to the line's amount.
        /// Any amount left after the last line goes on it, as in Rock, though Rock's block never asks for more than
        /// the transaction's total.
        /// </summary>
        public static List<RefundableLine> Split( IReadOnlyList<RefundableLine> lines, decimal amount )
        {
            var refund = new List<RefundableLine>();
            var remaining = amount;
            foreach ( var line in lines.Where( l => l.Amount > 0 ) )
            {
                var take = remaining >= line.Amount ? line.Amount : remaining;
                refund.Add( line with { Amount = 0 - take } );
                remaining -= take;
                if ( remaining <= 0.0m )
                {
                    break;
                }
            }

            if ( remaining > 0 && refund.Count > 0 )
            {
                refund[refund.Count - 1] = refund[refund.Count - 1] with { Amount = refund[refund.Count - 1].Amount + remaining };
            }

            return refund;
        }

        /// <summary>
        /// The day's refund batch for the payment, as FinancialBatchService.GetForNewTransaction finds or makes it:
        /// named by the payment and the refund suffix, from midnight to midnight (the Test Gateway has no batch time offset).
        /// </summary>
        private DataRow Batch( string paymentSuffix, DateTime when, object by )
        {
            var name = " " + paymentSuffix + BatchNameSuffix;
            name = name.Length > 50 ? name.Substring( 0, 50 ) : name;
            var day = when.Date;
            if ( _batchByNameAndDay.TryGetValue( (name, day), out var batch ) )
            {
                return batch;
            }

            batch = _batches.Rows.Add( _nextBatchId++, name, day, day.AddDays( 1 ), day < _anchor.Date.AddDays( -OpenBatchDays ) ? BatchClosed : BatchOpen, 0m, false,
                Stable( $"batch:{name}:{day:yyyy-MM-dd}" ), _foreignKey, when, when, by, by );
            _batchByNameAndDay[(name, day)] = batch;
            return batch;
        }

        private Guid Stable( string key ) => SeededRandom.StableGuid( $"refund-writer:{_foreignKey}:{key}" );
    }
}
