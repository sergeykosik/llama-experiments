using AccountsPrep.Common.DB.UoW;
using Microsoft.Practices.Unity;
using Newtonsoft.Json;
using OCRex.SecretsManager;
using OCRex.Web.DocuRec.ServiceModel.Endpoints;
using OCRex.Web.DocuRec.ServiceModel.Endpoints.AccountsPrep;
using SquareSets.Business.DBModel;
using SquareSets.Business.Entities;
using SquareSets.Business.Models;
using SquareSets.Business.Repository;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using SS = ServiceStack;

namespace SquareSets.Business.Services
{
    public class AutoEntryService
    {
        public static TResponse PostToAE<TResponse>(SS.IReturn<TResponse> requestDto, ICredentials credentials = null, string verb = SS.HttpMethods.Post)
        {
            return GetFromAE(requestDto, credentials, verb);
        }

        public static TResponse GetFromAE<TResponse>(SS.IReturn<TResponse> requestDto, ICredentials credentials = null, string verb = SS.HttpMethods.Get)
        {
            using (var scope = SS.Text.JsConfig.BeginScope())
            {
                scope.AssumeUtc = true;
                scope.AlwaysUseUtc = true;
                scope.DateHandler = SS.Text.DateHandler.ISO8601;

                using (SS.JsonServiceClient jsonClient = new SS.JsonServiceClient(SqsSettings.AEApiUrl))
                {
                    if (credentials != null)
                    {
                        jsonClient.Credentials = credentials;
                    }
                    else
                    {
                        foreach (string name in HttpContext.Current.Request.Cookies)
                            if (name.StartsWith("ss-"))
                            {
                                HttpCookie cookie = HttpContext.Current.Request.Cookies[name];
                                jsonClient.SetCookie(cookie.Name, cookie.Value);
                            }
                    }
                    

                    try
                    {
                        if (verb == SS.HttpMethods.Get)
                            return jsonClient.Get(requestDto);
                        else if (verb == SS.HttpMethods.Post)
                            return jsonClient.Post(requestDto);
                        else
                            throw new ApplicationException("Unknown verb: " + verb);
                    }
                    catch (SS.WebServiceException ex)
                    {
                        if (ex.StatusCode == 401 || ex.StatusCode == 403)
                        {
                            throw new LoginRequiredException();
                        }
                        //todo handle exception
                        throw;
                    }
                }
            }
        }

        public AutoEntryLoginResult Login(Guid token)
        {
            AELoginToken loginToken = AutoEntryRepository.GetLoginToken(token);
            if (loginToken == null || loginToken.LoginDT.HasValue || (DateTime.Now - loginToken.CreatedDT).TotalSeconds > 30)
                return new AutoEntryLoginResult { TokenNotFound = true };

            AutoEntryLoginResult result = new AutoEntryLoginResult();

            using (IUnitOfWork uow = UnitOfWorkFactory.Create())
            {
                Practice userPractice = null;

                PermissionCheckResponse permissionCheck =
                  GetFromAE(new PermissionCheckRequest { UserId = loginToken.AEUserId, CompanyId = (int)loginToken.AECompanyId, IsIncludeOrganisations = true }, null, SS.HttpMethods.Post);
                if (loginToken.IsAdmin)
                {
                    if (!permissionCheck.CanAdmin)
                        throw new SquareSetsException("No admin permission");
                }
                else if (permissionCheck.CompanyAccess?.CanViewBankStatements != true)
                    throw new SquareSetsException("No permission");

                AppUser user = AppUserRepository.GetAppUserByAutoEntryId(loginToken.AEUserId);

                if (user == null) //create user if not exists
                {
                    user = new AppUser();
                    user.Guid = Guid.NewGuid();
                    user.AutoEntryId = loginToken.AEUserId;
                    user.Email = permissionCheck.Email;
                    user.EmailVerification = Guid.NewGuid();
                    user.FirstName = permissionCheck.FirstName ?? "";
                    user.LastName = permissionCheck.LastName ?? "";
                    user.Password = "@@@";
                    user.PasswordHash = "hash";
                    user.Salt = "none";
                    user.Status = AppUserStatus.Active;
                    user.Role = AppUserRole.PracticeStaff;
                    user.CreatedDT = DateTime.Now;
                    user.ModifiedDT = DateTime.Now;
                    AppUserRepository.InsertAppUser(user);
                }
                else
                {
                    if (user.Email != permissionCheck.Email || user.FirstName != permissionCheck.FirstName || user.LastName != permissionCheck.LastName)
                        AppUserRepository.UpdateAppUser(user);
                }

                if (loginToken.IsAdmin)
                {
                    result.IsAdmin = true;
                }
                else
                {
                    PermissionCheckResponse.Organisation org = permissionCheck.Organisations.FirstOrDefault(o => o.Id > 0); //take first organisation from the list, maybe its not the best solution

                    if (org != null)
                    {
                        userPractice = PracticeRepository.GetPracticeByAutoEntryId(org.Id);

                        if (userPractice == null)
                        {
                            userPractice = new Practice { AutoEntryId = org.Id, CompanyName = org.Name ?? "empty name", CreatedDT = DateTime.Now, PracticeType = PracticeType.Normal, Guid = Guid.NewGuid() };
                            PracticeRepository.InsertPractice(userPractice);
                        }
                    }

                    GetCompanyDetailsResponse companyDetails = GetFromAE(new GetCompanyDetails { CompanyId = loginToken.AECompanyId });
                    Practice companyPractice = PracticeRepository.GetPracticeByAutoEntryId(companyDetails.OrganisationId);

                    if (org?.Id == companyDetails.OrganisationId)
                        companyPractice = userPractice;

                    if (companyPractice == null)
                    {
                        companyPractice = new Practice { AutoEntryId = companyDetails.OrganisationId, CompanyName = companyDetails.CompanyName + " practice", CreatedDT = DateTime.Now, PracticeType = PracticeType.Normal, Guid = Guid.NewGuid() };
                        PracticeRepository.InsertPractice(companyPractice);
                    }


                    Client client = ClientRepository.GetClientByAutoEntryId(loginToken.AECompanyId);
                    if (client != null)
                    {
                        if (!String.IsNullOrEmpty(companyDetails.DateFormat))
                            client.DateFormat = companyDetails.DateFormat == "DD-MM-YYYY" ? DateFormat.DDMMYYYY : DateFormat.MMDDYYYY;

                        Country country = PracticeRepository.GetCountries().FirstOrDefault(c => c.Code == companyDetails.CountryCode);
                        if (country == null)
                            throw new SquareSetsException($"Country {companyDetails.CountryCode} is not found");

                        if (country.Id == Const.USA)
                            client.IsNonVatRegistered = true;
                        else
                            client.IsNonVatRegistered = companyDetails.IsNonVatRegistered;

                        client.CurrencyCode = companyDetails.CurrencyCode;
                        client.CompanyName = companyDetails.CompanyName;
                        client.DateFormat = companyDetails.DateFormat == "DD-MM-YYYY" ? DateFormat.DDMMYYYY : DateFormat.MMDDYYYY;
                        client.CountryId = country.Id;

                        ClientRepository.UpdateClient(client);

                        result.ClientId = client.Guid;

                        GetFromAE(new CompanySetupCallback { ClientId = client.Id, CompanyId = loginToken.AECompanyId });
                    }
                }

                uow.Commit();

                result.User = user;
                result.AppUserId = user.Id;
                result.PracticeId = userPractice?.Guid;
            }


            return result;
        }


        [Dependency]
        public IUnitOfWorkFactory UnitOfWorkFactory { get; set; }

        [Dependency]
        public IPracticeRepository PracticeRepository { get; set; }

        [Dependency]
        public IClientRepository ClientRepository { get; set; }

        [Dependency]
        public IAutoEntryRepository AutoEntryRepository { get; set; }

        [Dependency]
        public IAppUserRepository AppUserRepository { get; set; }
        
    }

    public class AutoEntryLoginResult
    {
        public bool TokenNotFound { get; set; }
        public bool IsAdmin { get; set; }

        public int AppUserId { get; set; }
        public Guid? ClientId { get; set; }
        public AppUser User { get; set; }
        public Guid? PracticeId { get; set; }
    }
}
