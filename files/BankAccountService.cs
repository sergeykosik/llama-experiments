using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SquareSets.Business.Entity;
using AccountsPrep.Common.Csv;
using AccountsPrep.Common.ListBase;
using SquareSets.Business.Repository;
using AccountsPrep.Common.DB.UoW;
using AccountsPrep.Common;
using SquareSets.Business.DBModel;
using SquareSets.Business.Services;
using SquareSets.Business;
using SquareSets.Business.Models;
using Microsoft.Practices.Unity;

namespace SquareSets.Business.Services
{
    public class BankAccountService : /*ServiceBase, */IBankAccountService
    {
        private ClientInfoModel clientInfo;
        public BankAccountService(IClientInfoModelFactory clientInfoFactory)
        {
            clientInfo = clientInfoFactory.GetClientInfo();
        }

        /// <summary>
        /// Updates account balances from journal entries
        /// </summary>
        public void UpdateAccountBalances(ClientId clientId)
        {
            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                Period[] periods = PeriodRepository.GetClientPeriods(clientId).OrderBy(p => p.EndDate).ToArray();

                //get the first period opening balances. We will save them temporarily for use in CreateOpeningBalances
                Period firstPeriod = periods.First();
                PeriodBalance[] firstPeriodBalances = BankAccountRepository.GetPeriodBalancesByPeriod(firstPeriod.Id);
                foreach (PeriodBalance pb in firstPeriodBalances)
                {
                    pb.Balance = 0; //reset its balance, but do not chage opening balances
                    if (pb.OpeningBalance == 0 && pb.OpeningStatementBalance.GetValueOrDefault() == 0)
                        BankAccountRepository.DeletePeriodBalance(pb);
                    else
                        BankAccountRepository.UpdatePeriodBalance(pb);
                }

                //delete all period balances. We will recalculate them from scratch
                foreach (Period period in periods.Skip(1)) //do not delete very first period, which we have just updated above
                    BankAccountRepository.DeletePeriodBalancesByPeriod(period.Id);

                List<PeriodBalance> lastPeriodBalances = new List<PeriodBalance>();
                List<AccountDetails.Account> lastPeriodAccounts = new List<AccountDetails.Account>();
                List<BankStatementReport> lastPeriodStatements = new List<BankStatementReport>();

                List<Period> currentAndPriorPeriods = new List<Period>();

                foreach (Period period in periods)
                {
                    //create opening balances, because they will be required by GetAccountDetails below
                    CreateOpeningBalances(lastPeriodAccounts, lastPeriodBalances, lastPeriodStatements, period.Id);
                    lastPeriodBalances = new List<PeriodBalance>();
                    lastPeriodStatements = new List<BankStatementReport>();

                    currentAndPriorPeriods.Add(period);
                    ClientInfoModel clientInfo = new ClientInfoModel { Id = clientId, CurrentPeriod = period, CurrentAndPriorPeriods = currentAndPriorPeriods, Client = this.clientInfo.Client };
                    IBankService bs = CustomClientInfoFactory.Resolve<IBankService>(clientInfo);
                    AccountDetails ad = bs.GetAccountDetails(null, false);

                    if (ad.Accounts.Sum(o => o.TotalBalance) != 0)
                        throw new SquareSetsException("Balances do not net to nil");

                    //delete opening balances. We will insert them again below
                    BankAccountRepository.DeletePeriodBalancesByPeriod(period.Id);

                    foreach (AccountDetails.Account account in ad.Accounts)
                    {
                        if (account.TotalBalance != 0 || account.OpeningBalance.GetValueOrDefault() != 0 || account.OpeningStatementBalance.GetValueOrDefault() != 0)
                        {
                            PeriodBalance balance = new PeriodBalance();
                            balance.AccountId = account.Id;
                            balance.PeriodId = period.Id;
                            balance.OpeningBalance = account.OpeningBalance.GetValueOrDefault();
                            balance.OpeningStatementBalance = account.OpeningStatementBalance;
                            balance.Balance = account.TotalBalance;
                            BankAccountRepository.InsertPeriodBalance(balance);

                            lastPeriodBalances.Add(balance);
                        }
                        if (account.IsPostingAccount)
                            lastPeriodStatements.Add(bs.GetBankStatementReport(account.Id));
                    }
                    lastPeriodAccounts = ad.Accounts;
                }
                //if (hasChanges)
                //          RoundingService.Round_New_New(periodId);
                uow.Commit();
            }
        }

        private void CreateOpeningBalances(List<AccountDetails.Account> accounts, List<PeriodBalance> lastPeriodBalances, List<BankStatementReport> lastPeriodStatements,
          int periodId)
        {
            if (lastPeriodBalances == null || lastPeriodBalances.Count == 0)
                return;

            foreach (AccountDetails.Account account in accounts)
            {
                decimal openingBalance = 0;
                decimal? openingStatementBalance = null;

                if (account.IsPostingAccount)
                {
                    openingStatementBalance = lastPeriodStatements.FirstOrDefault(acc => acc.BankAccountId == account.Id)?.TotalBalance;
                }
                if (ClassIds.ProfitAndLoss.Contains(account.ClassId))
                {
                    openingBalance = 0;
                }
                else if (account.Id == this.clientInfo.Client.RetainedEarningsAccountId)
                {
                    //collect all P&L balances
                    HashSet<int> plAccountIds = new HashSet<int>(accounts.Where(a => ClassIds.ProfitAndLoss.Contains(a.ClassId)).Select(a => a.Id));
                    openingBalance = (lastPeriodBalances?.Where(b => plAccountIds.Contains(b.AccountId)).Sum(b => (decimal?)b.Balance)).GetValueOrDefault();

                    openingBalance += (lastPeriodBalances?.FirstOrDefault(b => b.AccountId == account.Id)?.Balance).GetValueOrDefault(); //add this account balance
                }
                else
                    openingBalance = (lastPeriodBalances?.FirstOrDefault(b => b.AccountId == account.Id)?.Balance).GetValueOrDefault();


                if (openingBalance != 0 || openingStatementBalance.GetValueOrDefault() != 0)
                {
                    PeriodBalance balance = new PeriodBalance();
                    balance.AccountId = account.Id;
                    balance.PeriodId = periodId;
                    balance.OpeningBalance = openingBalance;
                    balance.OpeningStatementBalance = openingStatementBalance;
                    BankAccountRepository.InsertPeriodBalance(balance);
                }
            }

        }


        internal virtual List<string> ValidateBankAccount(BankAccount account) //any other validations?
        {
            List<string> errors = new List<string>();

            int? id = BankAccountRepository.GetAccountIdByCode(account.Code, clientInfo.Id);
            if (id != null && id != account.Id)
                errors.Add("Code " + account.Code + " already exists");

            ClassModel cl = PeriodDataContainer.PlainClassesDict.GetValueOrDefault(account.ClassId);

            if (cl == null)
            {
                errors.Add("Class " + account.ClassId + " does not exist");
                return errors;
            }

            if (cl.Class == null || cl.Group == null)
            {
                errors.Add("Account can't belong to the top level class"); //todo get better message
                return errors;
            }

            if (cl.AllChildren.Count > 0)
            {
                errors.Add("Account must belong to the lowest class"); //todo get better message
            }

            if (account.IsPostingAccount)
            {
                int groupId = cl.Group.Id;
                if (groupId != Const.AssetsGroupId && groupId != Const.LiabilitiesGroupId)
                    errors.Add("Bank account must belong to assets or liabilities");
            }

            return errors;
        }

        public void SaveBankAccount(BankAccount account)
        {
            ActivityAction action;
            BankAccount dbAccount;
            bool changedPlToBs = false;

            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                List<string> errors = ValidateBankAccount(account);
                if (errors.Count > 0)
                    throw new SquareSetsException(errors);

                if (account.Id > 0)
                {
                    dbAccount = BankAccountRepository.GetBankAccountById(account.Id);

                    if (account.ClassId != dbAccount.ClassId)
                    {
                        errors = ValidateChangeClass(new[] { account.Id }, account.ClassId, out changedPlToBs);
                        if (errors.Count > 0)
                            throw new SquareSetsException(errors);
                    }

                    //if (dbAccount.IsPostingAccount != account.IsPostingAccount)//cannot change "Bank Account (for posting purposes)" because there are entries on the account
                    //if (IsBankAccountInUse(account.Id))
                    //throw new SquareSetsException("Cannot change \"Bank Account (for posting purposes)\" because there are entries on the account");
                    action = ActivityAction.Account_Update;
                }
                else
                {
                    dbAccount = new BankAccount();
                    dbAccount.Guid = Guid.NewGuid();
                    dbAccount.ClientId = clientInfo.Id;
                    dbAccount.IsActive = true;
                    dbAccount.IsPostingAccount = account.IsPostingAccount;
                    dbAccount.CreatedDT = DateTime.Now;
                    action = ActivityAction.Account_Add;
                }
                dbAccount.Code = account.Code;
                dbAccount.Name = account.Name;
                dbAccount.ClassId = account.ClassId;
                dbAccount.DefaultExpenseTaxRateId = account.DefaultExpenseTaxRateId;
                dbAccount.DefaultSalesTaxRateId = account.DefaultSalesTaxRateId;
                dbAccount.ModifiedDT = DateTime.Now;

                if (account.Id > 0)
                    BankAccountRepository.UpdateAccount(dbAccount);
                else
                    BankAccountRepository.InsertAccount(dbAccount);

                if (changedPlToBs)
                    UpdateAccountBalances(clientInfo.Id);

                LogService.LogAction(action, dbAccount.Id, clientInfo.Id);
                /*
                if (account.Id <= 0 && account.IsPostingAccount)
                {
                  SetFavAccountForAllUsers(dbAccount);
                }*/
                account.Guid = dbAccount.Guid;
                account.Id = dbAccount.Id;

                uow.Commit();
            }
        }

        /// <summary>
        /// Assuming the account is new
        /// </summary>
        public virtual void SetFavAccountForAllUsers(BankAccount account)
        {
            AppUser[] clientStaff = ClientRepository.GetClientStaff(clientInfo.Id);
            foreach (AppUser s in clientStaff)
                BankAccountRepository.ToggleFavourite(account.Id, s.Id);
        }

        public List<string> ValidateChangeClass(IEnumerable<int> ids, int classId, out bool changedPlToBs)
        {
            List<string> errors = new List<string>();
            changedPlToBs = false;

            Client client = ClientRepository.GetClient(clientInfo.Id);

            ClassModel cl = PeriodDataContainer.PlainClassesDict[classId];

            if (cl.AllChildrenIds.Count > 0)
                errors.Add("Can assign only to the child class");

            foreach (int id in ids)
            {
                BankAccount dbAccount = BankAccountRepository.GetBankAccountById(id);

                if (client.RetainedEarningsAccountId == id && cl.Group.Id != Const.EquityGroupId)
                    errors.Add(String.Format("Account {0} is used for retained earnings and must be an Equity class", dbAccount.DisplayName));
                //if ((bsMap.Any(o => o.DestAccountId == id) || bsMap.Any(o => o.SrcAccountId == id)) && !Const.BalanceSheetGroupIds.Contains(cl.Group.Id))
                //errors.Add(String.Format("Account {0} is used as a carry forward account and must be a Balance Sheet class", dbAccount.DisplayName));
                //Account  " + dbAccount.DisplayName + " is used as carry forward and must be in Assets/Liabilities");
                if (dbAccount.IsPostingAccount && !(Const.AssetsGroupId == cl.Group.Id || Const.LiabilitiesGroupId == cl.Group.Id))
                    errors.Add(String.Format("Account {0} is a bank account and must be an Asset/Liabilities class", dbAccount.DisplayName));

                ClassModel currentClass = PeriodDataContainer.PlainClassesDict[dbAccount.ClassId];
                if (currentClass.Group.Id == Const.ProfitLossGroupId && Const.BalanceSheetGroupIds.Contains(cl.Group.Id)
                    || cl.Group.Id == Const.ProfitLossGroupId && Const.BalanceSheetGroupIds.Contains(currentClass.Group.Id))
                    changedPlToBs = true;
            }

            return errors;
        }


        public void ChangeClass(IEnumerable<int> ids, int classId)
        {
            bool changedPlToBs = false;
            List<string> errors = ValidateChangeClass(ids, classId, out changedPlToBs);
            if (errors.Count > 0)
                throw new SquareSetsException(errors);

            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                foreach (int id in ids)
                {
                    BankAccount dbAccount = BankAccountRepository.GetBankAccountById(id);
                    dbAccount.ClassId = classId;
                    dbAccount.ModifiedDT = DateTime.Now;
                    BankAccountRepository.UpdateAccount(dbAccount);
                }

                if (changedPlToBs)
                    UpdateAccountBalances(clientInfo.Id);

                LogService.LogAction(ActivityAction.Account_Update, ids, clientInfo.Id);

                uow.Commit();
            }
        }

        public bool ToggleAccount(int id)
        {
            //assuming client and period was automatically validated by getting Id by Guid.
            BankAccount dbAccount = BankAccountRepository.GetBankAccountById(id);

            if (dbAccount.IsActive) //cannot deactivate if has balances
            {
                if (dbAccount.IsPostingAccount)
                    throw new SquareSetsException("You can't deactivate a bank account.");

                ProcessedBalance pb = PeriodBalanceService.GetProcessedBalances(); //TODOPeriodBalanceService.GetProcessedBalances(null);
                if (pb.HasBalances(id))// || dbAccount.OpeningStatementBalance.HasValue)
                    throw new SquareSetsException("You can't deactivate an account that has an entry in it (any balance) in any period.");

                Client client = ClientRepository.GetClient(clientInfo.Id);

                if (id == client.TaxOnSalesAccountId || id == client.TaxOnExpensesAccountId ||
                    id == client.TotalInSalesAccountId || id == client.TotalInExpensesAccountId)
                    throw new SquareSetsException("You can't deactivate an account that is used in financial settings.");

                if (id == client.RetainedEarningsAccountId)
                    throw new SquareSetsException("You can't deactivate an account that is used for retained earnings (roll forward settings).");

                //if the account used in normal cheques then it will have a balance, so if we came to this point then only outstanding (Q: mean cheques from last period?) cheques left
                int[] chequeIds = ChequesRepository.GetChequeIdsByBankAccount(id);
                int[] splitIds = ChequesRepository.GetChequeSplitIdsByBankAccount(id);
                if (chequeIds.Length > 0 || splitIds.Length > 0)
                    throw new SquareSetsException("You can't deactivate an account that contains outstanding cheques or deposits.");
            }
            dbAccount.IsActive = !dbAccount.IsActive;
            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                BankAccountRepository.UpdateAccount(dbAccount);
                LogService.LogAction(ActivityAction.Account_Update, id, clientInfo.Id);
                
                uow.Commit();
            }
            return dbAccount.IsActive;
        }

        public void DeleteAccounts(IEnumerable<int> ids) //can be optimizied when 1 account is deleted
        {
            List<string> errors = new List<string>();

            ProcessedBalance pb = PeriodBalanceService.GetProcessedBalances();

            BankStatement[] bankStatements = JournalRepository.GetBankStatements(clientInfo.CurrentPeriod.Id, null, null, null);
            ChequeBatch[] chequeBatches = ChequesRepository.GetChequeBatchesWithoutLines(null, new[] { (PeriodId)clientInfo.CurrentPeriod.Id }, null);

            Client client = ClientRepository.GetClient(clientInfo.Id);

            //if account is used in journals or lines it will have not null balance. However it can be used in deleted or draft journals, that we are going to check
            bool settingsChanged = false;

            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                foreach (int id in ids)
                {
                    BankAccount dbAccount = BankAccountRepository.GetBankAccountById(id);

                    if (pb.HasBalances(id))
                        errors.Add(String.Format("Account {0} contains balances.", dbAccount.DisplayName));
                    if (client.RetainedEarningsAccountId == id)
                        errors.Add(String.Format("Account {0} is used for retained earnings", dbAccount.DisplayName));
                    //if (bsMap.Any(o => o.DestAccountId == id) || bsMap.Any(o => o.SrcAccountId == id))
                    //errors.Add(String.Format("Account {0} is used as a carry forward account.", dbAccount.DisplayName));

                    if (client.TaxOnExpensesAccountId == id)
                        errors.Add(String.Format("Account {0} is used as a tax account for cash expenses", dbAccount.DisplayName));
                    if (client.TaxOnSalesAccountId == id)
                        errors.Add(String.Format("Account {0} is used as a tax account for cash sales", dbAccount.DisplayName));


                    if (client.TotalInExpensesAccountId == id)
                    {
                        client.TotalInExpensesAccountId = null;
                        settingsChanged = true;
                    }
                    if (client.TotalInSalesAccountId == id)
                    {
                        client.TotalInSalesAccountId = null;
                        settingsChanged = true;
                    }


                    int[] bsBatchNums = bankStatements.Where(o => o.Status != (short)EntryStatus.Posted && o.BankAccountId == id).Select(o => o.BatchNumber).ToArray();
                    int[] chqBatchNums = chequeBatches.Where(o => o.IsCheque && o.Status != (short)EntryStatus.Posted && o.BankAccountId == id).Select(o => o.BatchNumber).ToArray();
                    int[] depBatchNums = chequeBatches.Where(o => !o.IsCheque && o.Status != (short)EntryStatus.Posted && o.BankAccountId == id).Select(o => o.BatchNumber).ToArray();

                    if (bsBatchNums.Length > 0)
                        errors.Add(String.Format("Account {0} is used in bank statement(s): {1}", dbAccount.DisplayName, bsBatchNums.ToCommaSeparatedString()));
                    if (chqBatchNums.Length > 0)
                        errors.Add(String.Format("Account {0} is used in {1}: {2}", dbAccount.DisplayName, Resources.Common.Cheques.ToLower(), chqBatchNums.ToCommaSeparatedString()));
                    if (depBatchNums.Length > 0)
                        errors.Add(String.Format("Account {0} is used in deposits: {1}", dbAccount.DisplayName, depBatchNums.ToCommaSeparatedString()));
                }

                if (errors.Count == 0) //if validation is fine then delete the accounts
                {
                    if (settingsChanged)
                        ClientRepository.UpdateClient(client);

                    foreach (int id in ids)
                    {
                        BankAccountRepository.DeleteBankAccount(new BankAccount { Id = id });
                    }

                    LogService.LogAction(ActivityAction.Account_Delete, ids, clientInfo.Id);

                    uow.Commit();
                }
            }
            if (errors.Count > 0)
                throw new SquareSetsException(errors);
        }

        [Dependency]
        public IUnitOfWorkFactory UnitOfWorkFactory { get; set; }
        [Dependency]
        public IBankAccountRepository BankAccountRepository { get; set; }
        [Dependency]
        public IClientRepository ClientRepository { get; set; }
        [Dependency]
        public IJournalRepository JournalRepository { get; set; }
        [Dependency]
        public IPeriodRepository PeriodRepository { get; set; }
        [Dependency]
        public IChequesRepository ChequesRepository { get; set; }
        [Dependency]
        public ILogService LogService { get; set; }

        [Dependency]
        public IPeriodBalanceService PeriodBalanceService { get; set; }

        [Dependency]
        public IPeriodDataContainer PeriodDataContainer { get; set; }

        [Dependency]
        public IBankService BankService { get; set; }

    }
}

