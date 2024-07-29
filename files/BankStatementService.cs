using AccountsPrep.Common.DB.UoW;
using Microsoft.Practices.Unity;
using SquareSets.Business.DBModel;
using SquareSets.Business.Entities;
using SquareSets.Business.Repository;
using System;
using System.Collections.Generic;
using System.Linq;
using AccountsPrep.Common;
using System.Text;
using System.Threading.Tasks;

namespace SquareSets.Business.Services
{
    public class BankStatementService : IBankStatementService
    {
        ClientInfoModel clientInfo;
        public BankStatementService(IClientInfoModelFactory clientInfoFactory)
        {
            clientInfo = clientInfoFactory.GetClientInfo();
        }

        private List<string> ValidateTransfers(BankStatement bs, Dictionary<int, BankAccount> dictAccounts)
        {
            return ChequeService.ValidateTransfers(bs.Lines, bs.BankAccountId, dictAccounts);
        }

        public virtual List<string> ValidateBankStatement(BankStatement bs)
        {
            //int periodId = clientInfo.CurrentPeriod.Id;
            Client client = ClientRepository.GetClient(clientInfo.Id);
            BankAccount[] bankAccounts = BankAccountRepository.GetAllBankAccounts(client.Id);
            Dictionary<int, BankAccount> dictBankAccounts = bankAccounts.ToDictionary(o => o.Id);

            TaxRate[] taxRates = null;

            List<string> errors = new List<string>();
            
            if (bs.PeriodId != clientInfo.CurrentPeriod.Id)
                errors.Add("Periods do not match");

            if (bs.Lines.Any(o => o.TaxRateId.HasValue) || bs.Lines.Where(o => o.Splits != null).Any(o => o.Splits.Any(x => x.TaxRateId.HasValue))) //load tax rates if exist
            {
                taxRates = ClientRepository.GetTaxRates(client.Id);
                bool hasSales = bs.Lines.Any(o => o.TaxRateId.HasValue && o.Amount > 0) || bs.Lines.Where(o => o.Splits != null).Any(o => o.Splits.Any(s => s.TaxRateId.HasValue && s.Amount > 0));
                bool hasExpenses = bs.Lines.Any(o => o.TaxRateId.HasValue && o.Amount <= 0) || bs.Lines.Where(o => o.Splits != null).Any(o => o.Splits.Any(s => s.TaxRateId.HasValue && s.Amount <= 0));

                string msg = "";
                if (hasSales && !client.TaxOnSalesAccountId.HasValue && hasExpenses && !client.TaxOnExpensesAccountId.HasValue)
                    msg = "Accounts designated for Tax on Sales and Tax on Expense are not yet set up.";
                else if (hasExpenses && !client.TaxOnExpensesAccountId.HasValue)
                    msg = "Account designated for Tax on Expense is not yet set up.";
                else if (hasSales && !client.TaxOnSalesAccountId.HasValue)
                    msg = "Account designated for Tax on Sales is not yet set up.";

                if (!String.IsNullOrEmpty(msg))
                    errors.Add(msg + " Please save as draft for now and then set up these accounts in Settings > Financial Information. After that you can return and post the draft.");
            }

            List<int> chequeIds = bs.Lines.Where(o => o.ChequeId.HasValue).Select(o => o.ChequeId.Value).ToList();
            List<ChequeMatchList> chequeMatches = ChequesRepository.GetChequesMatches(chequeIds, null, null, null).ToList();


            errors.AddRange(ValidateTransfers(bs, dictBankAccounts));

            foreach (BankStatementLine line in bs.Lines)
            {
                if (clientInfo.CurrentPeriod.OpeningBalancesDate.HasValue && clientInfo.CurrentPeriod.OpeningBalancesDate > clientInfo.CurrentPeriod.StartDate.Value)
                    if (line.Date >= clientInfo.CurrentPeriod.StartDate && line.Date < clientInfo.CurrentPeriod.OpeningBalancesDate.Value)
                        errors.Add($"Line {line.Details} date must be on or later than the date opening balances were imported ({clientInfo.CurrentPeriod.OpeningBalancesDate.Value.ToShortDateString()})");

                if (line.Date > clientInfo.CurrentPeriod.EndDate || line.Date < clientInfo.CurrentPeriod.StartDate.Value)
                    errors.Add("Line " + line.Details + " date is outside of the current period");

                if (line.Amount == 0)
                    errors.Add("Line " + line.Details + " amount is empty");
                if (String.IsNullOrWhiteSpace(line.Details))
                    errors.Add("Line " + line.Id + " details is empty");

                if (line.BankAccountId.HasValue && line.ChequeId.HasValue)
                    errors.Add("Line " + line.Details + " has both bank account and cheque / deposit"); //this also checks if "bank statement line once transferred cannot be matched to a cheque"

                if (line.BankAccountId.HasValue && !Utils.IsListNullOrEmpty(line.Splits))
                    errors.Add("Line " + line.Details + " has both bank account and splits");

                if (line.TaxRateId.HasValue)
                {
                    TaxRate tr = taxRates.First(o => o.Id == line.TaxRateId);
                    if (line.Amount > 0 && (((TaxRateAvailability)tr.Availability) & TaxRateAvailability.SalesBatches) == 0)
                        errors.Add("Line " + line.Details + " is receipt but uses expense tax");
                    if (line.Amount < 0 && (((TaxRateAvailability)tr.Availability) & TaxRateAvailability.ExpenseBatches) == 0)
                        errors.Add("Line " + line.Details + " is payment but uses sales tax");
                }


                BankAccount ba = dictBankAccounts.GetValueOrDefault(line.BankAccountId);
                if (ba == null)
                {
                    //that could be split or cheque match, if not - then error
                    if (Utils.IsListNullOrEmpty(line.Splits) && line.ChequeId == null)
                        errors.Add("Line " + line.Details + " has no bank account");
                    //continue;
                }

                //cannot assign line to the bank account where entry itself is
                if (line.BankAccountId.HasValue && line.BankAccountId.Value == bs.BankAccountId)
                    errors.Add("Line " + line.Details + " cannot be assigned to the same bank account as its entry");

                //validate splits
                if (!Utils.IsListNullOrEmpty(line.Splits))
                {
                    if (line.Matches != null && line.Matches.Count > 0)
                        errors.Add("Line " + line.Details + " has both split and transfer");
                    if (line.Splits.Sum(o => o.Amount) != line.Amount)
                        errors.Add("Line " + line.Details + " split amount does not match");

                    foreach (BankStatementLineSplit splitLine in line.Splits)
                    {
                        BankAccount sba = dictBankAccounts.GetValueOrDefault(splitLine.BankAccountId);
                        if (sba == null)
                            errors.Add("Split line has no bank account");
                        else
                        {
                            if (sba.IsPostingAccount)
                                errors.Add("Split has posting account");
                        }
                        if (splitLine.Amount == 0)
                            errors.Add("Split line amount is empty");
                        if (String.IsNullOrWhiteSpace(splitLine.Details))
                            errors.Add("Split line details is empty");
                        if (splitLine.TaxRateId != null)
                        {
                            TaxRate tr = taxRates.First(o => o.Id == splitLine.TaxRateId);

                            if (line.Amount > 0 && (((TaxRateAvailability)tr.Availability) & TaxRateAvailability.SalesBatches) == 0)
                                errors.Add("Line " + line.Details + " is receipt but split uses expense tax");
                            if (line.Amount < 0 && (((TaxRateAvailability)tr.Availability) & TaxRateAvailability.ExpenseBatches) == 0)
                                errors.Add("Line " + line.Details + " is payment but split uses sales tax");
                        }
                    }
                }

                if (line.ChequeId.HasValue)
                {
                    if (line.TaxRateId != null)
                        errors.Add("Cannot match line " + line.Details + " because it contains tax");
                    if (line.Splits != null && line.Splits.Count > 0)
                        errors.Add("Cannot match line " + line.Details + " because it contains splits");

                    ChequeMatchList cheque = chequeMatches.FirstOrDefault(o => o.ChequeId == line.ChequeId);
                    if (cheque == null)
                        errors.Add(Resources.Common.Cheque + " for " + line.Details + " was not found");
                    else
                    {
                        if (cheque.IsCheque && line.Amount > 0)
                            errors.Add("Cannot match receipt with a cheque in line " + line.Details);
                        if (!cheque.IsCheque && line.Amount < 0)
                            errors.Add("Cannot match payment with a deposit in line " + line.Details);
                        if (Math.Abs(cheque.ChequeAmount.GetValueOrDefault()) != Math.Abs(line.Amount))
                            errors.Add((cheque.IsCheque ? Resources.Common.Cheque : "Deposit") + " amount " + cheque.ChequeAmount + " in line " + line.Details + " does not match line amount " + line.Amount);
                        if (cheque.BankEntryLineId != null && cheque.BankEntryLineId != line.Id)
                            errors.Add((cheque.IsCheque ? Resources.Common.Cheque : "Deposit") + " in line " + line.Details + " already reconciled");

                        chequeMatches.Remove(cheque); //remove once matched
                    }
                }
            }
            return errors;
        }


        public void SaveBankStatement(BankStatement bs, bool calcBalance)
        {
            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                bs.Lines.ForEach(o => { if (o.Id <= 0 && o.Guid == Guid.Empty) o.Guid = Guid.NewGuid(); });

                DateTime? unpostedMinDt = null, unpostedMaxDt = null;

                if (bs.Status == (short)EntryStatus.Unposted) //unposted journals take the line dates into consideration for min/max period date
                {
                    unpostedMaxDt = bs.Lines.Max(l => (DateTime?)l.Date);
                    unpostedMinDt = bs.Lines.Min(l => (DateTime?)l.Date);

                    if ((unpostedMaxDt.HasValue && unpostedMaxDt > clientInfo.CurrentPeriod.EndDate) ||
                        (unpostedMinDt.HasValue && unpostedMinDt < clientInfo.CurrentPeriod.StartDate))
                    {
                        throw new SquareSetsException("Line dates are outside of the current period");
                    }
                }

                if (bs.Status != (short)EntryStatus.Posted)//serialize lines into DraftData
                {
                    bs.Lines.ForEach(o => { o.Id = -(Math.Abs(o.Id)); /*o.ChequeId = null; o.Matches = null; */});//set Id to negative so it call insert rather than update, and reset matches and transfers
                    bs.DraftData = bs.SerializeLines();
                    bs.Lines = new List<BankStatementLine>(); //if draft we delete all lines. This can happen in case if we change status from posted to draft
                }
                else
                {
                    bs.DraftData = null;

                    List<string> errors = ValidateBankStatement(bs);
                    if (errors.Count > 0)
                        throw new SquareSetsException(errors);
                }

                bs.ModifiedDT = DateTime.Now;
                if (bs.Id > 0)
                {
                    AnalysisTaggingRepository.DeleteJournalAnalysisTags(bs.Id, clientInfo.Id);

                    JournalBase jb = JournalRepository.GetJournalBase(bs.Id, bs.PeriodId);
                    bs.CreatedDT = jb.CreatedDT;
                    bs.BatchNumber = jb.BatchNumber;
                    bs.Guid = jb.Guid;
                    JournalRepository.UpdateBankStatement(bs);

                    LogService.LogAction(ActivityAction.BankStatement_Update, bs.Id, clientInfo.Id);
                }
                else
                {
                    bs.CreatedDT = DateTime.Now;
                    bs.Guid = Guid.NewGuid();
                    JournalRepository.InsertBankStatement(bs);

                    LogService.LogAction(ActivityAction.BankStatement_Add, bs.Id, clientInfo.Id);
                }

                if (bs.Lines?.Count > 0)
                {
                    List<int> tagIds = bs.Lines.Where(l => l.AnalysisTagId.HasValue).Select(l => l.AnalysisTagId.Value).Distinct().ToList();
                    AnalysisTaggingRepository.InsertJournalAnalysisTags(bs.Id, clientInfo.Id, tagIds);
                }


                if (calcBalance)
                {
                    JournalRepository.UpdateBatchNumbers(clientInfo.Id);

                    BankAccountService.UpdateAccountBalances(clientInfo.Id);
                }
                uow.Commit();
            }
        }
        [Dependency]
        public IAnalysisTaggingRepository AnalysisTaggingRepository { get; set; }
        [Dependency]
        public IBankAccountRepository BankAccountRepository { get; set; }
        [Dependency]
        public IJournalRepository JournalRepository { get; set; }
        [Dependency]
        public IChequesRepository ChequesRepository { get; set; }
        [Dependency]
        public IClientRepository ClientRepository { get; set; }
        [Dependency]
        public IUnitOfWorkFactory UnitOfWorkFactory { get; set; }
        [Dependency]
        public IBankAccountService BankAccountService { get; set; }
        [Dependency]
        public IChequeService ChequeService { get; set; }
        [Dependency]
        public ILogService LogService { get; set; }
    }
}
