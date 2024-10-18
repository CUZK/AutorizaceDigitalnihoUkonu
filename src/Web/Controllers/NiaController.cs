using AutorizaceDigitalnihoUkonu.Web.SubmissionService;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Mvc;
using ITfoxtec.Identity.Saml2.Schemas;
using System;
using System.Collections.Generic;
using System.IdentityModel.Protocols.WSTrust;
using System.IdentityModel.Services;
using System.IdentityModel.Tokens;
using System.Security.Authentication;
using System.Security.Claims;
using System.ServiceModel;
using System.ServiceModel.Security;
using System.Text;
using System.Web;
using System.Web.Mvc;
using System.Xml;
using AutorizaceDigitalnihoUkonu.Web.Lib;
using System.Net.NetworkInformation;

namespace AutorizaceDigitalnihoUkonu.Web.Controllers
{
    [Authorize]
    public class NiaController : Controller
    {
        [HttpGet]
        public ActionResult Index()
        {
            return View();
        }

        #region Login, Logout
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Login(string returnUrl)
        {

            var authnRequest = new Saml2AuthnRequest(MvcApplication.Saml2Configuration)
            {
                RequestedAuthnContext = new RequestedAuthnContext
                {
                    Comparison = AuthnContextComparisonTypes.Minimum,
                    AuthnContextClassRef = new[] { "http://eidas.europa.eu/LoA/low" }
                },
                Extensions = new EidasExtensions()
            };
            var binding = new Saml2RedirectBinding();

            binding.SetRelayStateQuery(new Dictionary<string, string>
            {
                { "ReturnUrl", returnUrl ?? Url.Content("~/") }
            });

            return binding.Bind(authnRequest).ToActionResult();
        }

        [HttpPost]
        [AllowAnonymous]
        [ActionName("Process-Login")]
        public ActionResult ProcessLogin()
        {
            var authnResponse = new Saml2AuthnResponse(MvcApplication.Saml2Configuration);
            var httpRequest = Request.ToGenericHttpRequest(validate: true);

            httpRequest.Binding.ReadSamlResponse(httpRequest, authnResponse);

            if (authnResponse.Status != Saml2StatusCodes.Success)
            {
                throw new AuthenticationException($"SAML Response status: {authnResponse.Status}.");
            }

            httpRequest.Binding.Unbind(httpRequest, authnResponse);
            authnResponse.CreateSession();

            var returnUrl = httpRequest.Binding.GetRelayStateQuery().TryGetValue("ReturnUrl", out var rUrl) ? rUrl : Url.Content("~/");

            return Redirect(returnUrl);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout()
        {
            var logoutRequest = new Saml2LogoutRequest(MvcApplication.Saml2Configuration, ClaimsPrincipal.Current).DeleteSession();
            var binding = new Saml2RedirectBinding();

            return binding.Bind(logoutRequest).ToActionResult();
        }

        [HttpPost]
        [AllowAnonymous]
        [ActionName("Process-Logout")]
        public ActionResult ProcessLogout()
        {
            var httpRequest = Request.ToGenericHttpRequest(validate: true);
            httpRequest.Binding.Unbind(httpRequest, new Saml2LogoutResponse(MvcApplication.Saml2Configuration));

            return Redirect(Url.Content("~/"));
        }

        #endregion

        #region Autorizace
        /// <summary>
        /// Zahájí autorizaci digitálního úkonu
        /// Zjistí, jaké metovy má uživatel dostupné a vráítí View pro výběr metody
        /// Pokud je dostupná pouize jedna metoda (přihlášením), tak se přesměruje rovnou do NIA
        /// </summary>
        /// <param name="nazevDigitalnihoUkonu">Název digitálního úkonu</param>
        /// <param name="popisDigitalnihoUkonu">Popus digitálního úkonu</param>
        /// <param name="soubor">Soubor který se má autorizovat</param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SelectAuthorization(string nazevDigitalnihoUkonu, string popisDigitalnihoUkonu, HttpPostedFileBase soubor)
        {
            var bodyXml = $@"
                <ns:ADUStartRequest xmlns:ns=""urn:nia.ADU.start/request:v1"">
                    <ns:SePP>{User.Identity.Name}</ns:SePP>
                    <ns:HashSouboru>{soubor.GetHash()}</ns:HashSouboru>
                    <ns:NazevDigitalnihoUkonu>{nazevDigitalnihoUkonu}</ns:NazevDigitalnihoUkonu>
                    <ns:PopisDigitalnihoUkonu>{popisDigitalnihoUkonu}</ns:PopisDigitalnihoUkonu>
                    <ns:CertifikatHashBase64>{MvcApplication.NiaConfiguration.AduCertificateBase64}</ns:CertifikatHashBase64>
                </ns:ADUStartRequest>
            ";
            
            var responseBodyXml = CallSubmissionService("TR_ADU_START", bodyXml);

            var idUkonu = responseBodyXml.SelectSingleNode("//*[local-name()='IdUkonu']")?.InnerText;
            var zpusobyAutorizace = responseBodyXml.SelectSingleNode("//*[local-name()='MozneZpusobyAutorizace']")?.InnerText.Split(';');

            ViewBag.ResponseBodyXml = responseBodyXml.ToPrettyString();

            if (!responseStatusOK(responseBodyXml, out string detail))
            {
                ModelState.AddModelError("ERR_NIA", $"Chyba NIA: {detail}");
                return View();
            }

            if (string.IsNullOrWhiteSpace(idUkonu))
            {
                ModelState.AddModelError("ERR_NIA", "Chyba: Prázdné IdUkonu");
                return View();
            }

            if (zpusobyAutorizace?.Length == 1) // Pokud je jen jeden způsob autorizace, tak se přesměruje rovnou do NIA, protože to musí být metoda přihlášením
            {
                return Redirect(string.Format(MvcApplication.NiaConfiguration.AduAutorizacePodaniUrl, idUkonu, Url.Action("Adu", "Nia", null, Request.Url?.Scheme)));
            }

            ViewBag.IdUkonu = idUkonu;

            return View(zpusobyAutorizace);
        }

        /// <summary>
        ///  Pro předané IdUkonu a zvolený způsob autorizace zahájí vlastní autorizaci.
        ///  Pokud se má autorizovat přihlášením, tak se přesměruje do NIA
        ///  Pokud se má autorizovat SMS nbo Mobilním klíčem, tak se zavolá služba NIA a zobrazí se View pro zadání kódu
        /// </summary>
        /// <param name="idUkonu">ID úkonu</param>
        /// <param name="zpusobAutorizace">Způsob autorizace</param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Authorization(string idUkonu, string zpusobAutorizace)
        {
            if (zpusobAutorizace == "Autentizace")
            {
                return Redirect(string.Format(MvcApplication.NiaConfiguration.AduAutorizacePodaniUrl, idUkonu, Url.Action("Adu", "Nia", null, Request.Url?.Scheme)));
            }

            var bodyXml = $@"
                <ns:ADUSendCodeRequest xmlns:ns=""urn:nia.ADU.sendcode/request:v1"">
                    <ns:SePP>{User.Identity.Name}</ns:SePP>
                    <ns:KomunikacniKanal>{zpusobAutorizace}</ns:KomunikacniKanal>
                    <ns:IdUkonu>{idUkonu}</ns:IdUkonu>
                    <ns:CertifikatHashBase64>{MvcApplication.NiaConfiguration.AduCertificateBase64}</ns:CertifikatHashBase64>
                </ns:ADUSendCodeRequest>
            ";
            var responseBodyXml = CallSubmissionService("TR_ADU_SEND_CODE", bodyXml);

            if (!responseStatusOK(responseBodyXml, out string detail))
            {
                ModelState.AddModelError("ERR_NIA", $"Chyba NIA: {detail}");
            }

            ViewBag.IdUkonu = idUkonu;
            ViewBag.ResponseBodyXml = responseBodyXml.ToPrettyString();

            return View();
        }


        /// <summary>
        ///  Pro předané IdUkonu zašle uživatelem zadaný kód do NIA
        /// </summary>
        /// <param name="idUkonu">ID úkonu</param>
        /// <param name="kod">Autorizační kód</param>
        /// <returns></returns>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult ConfirmAuthorization(string idUkonu, string kod)
        {
            var hashKoduSha512 = Encoding.UTF8.GetBytes(kod).GetHash("System.Security.Cryptography.SHA512");
            var bodyXml = $@"
                <ns:ADUConfirmCodeRequest xmlns:ns=""urn:nia.ADU.confirmcode/request:v1"">
                    <ns:SePP>{User.Identity.Name}</ns:SePP>
                    <ns:HashKoduSHA512>{hashKoduSha512}</ns:HashKoduSHA512>
                    <ns:IdUkonu>{idUkonu}</ns:IdUkonu>
                    <ns:CertifikatHashBase64>{MvcApplication.NiaConfiguration.AduCertificateBase64}</ns:CertifikatHashBase64>
                </ns:ADUConfirmCodeRequest>
            ";
            var responseBodyXml = CallSubmissionService("TR_ADU_CONFIRM_CODE", bodyXml);

            ViewBag.ResponseBodyXml = responseBodyXml.ToPrettyString();

            if (!responseStatusOK(responseBodyXml, out string detail))
            {
                ModelState.AddModelError("ERR_NIA", $"Chyba NIA: {detail}");
                return View();
            }

            if (!vysledekOVereniOK(responseBodyXml, out string vysledek))
            {
                ModelState.AddModelError("ERR_VYSLEDEK", $"Ověření kódu selhalo, VysledekOvereni:{vysledek}");
                return View();
            }

            return RedirectToAction("Adu", new { Id = idUkonu});
        }

        /// <summary>
        /// Zjistí výsledek autorizace a zobrazí ho
        /// </summary>
        /// <param name="id">ID úkonu</param>
        /// <returns></returns>
        [HttpGet]
        public ActionResult Adu(string id)
        {
            var bodyXml = $@"
                <ns:ADUStatusRequest xmlns:ns=""urn:nia.ADU.status/request:v1"">
                    <ns:SePP>{User.Identity.Name}</ns:SePP>
                    <ns:IdUkonu>{id}</ns:IdUkonu>
                    <ns:CertifikatHashBase64>{MvcApplication.NiaConfiguration.AduCertificateBase64}</ns:CertifikatHashBase64>
                </ns:ADUStatusRequest>
            ";
            var responseBodyXml = CallSubmissionService("TR_ADU_STATUS", bodyXml);

            ViewBag.ResponseBodyXml = responseBodyXml.ToPrettyString();

            if (!responseStatusOK(responseBodyXml, out string detail))
            {
                ModelState.AddModelError("ERR_NIA", $"Chyba NIA: {detail}");
            }

            if (!vysledekOVereniOK(responseBodyXml, out string vysledek))
            {
                ModelState.AddModelError("ERR_VYSLEDEK", $"Autorizace selhala, VysledekOvereni:{vysledek}");
            }

            return View();
        }

        protected override void OnException(ExceptionContext filterContext)
        {
            filterContext.ExceptionHandled = true;

            ViewBag.ErrorMessage = $"{filterContext.Exception.GetType().Name}: {filterContext.Exception.Message}";

            filterContext.Result = new ViewResult
            {
                ViewName = "~/Views/Shared/Error.cshtml",
                ViewData = filterContext.Controller.ViewData
            };
        }
        #endregion

        #region Pomocné metody
        /// <summary>
        /// Vrátí, zda autorizace dopadla úspšně
        /// </summary>
        /// <param name="response">Response obdržená z NIA</param>
        /// <param name="vysledek">Hodnota elementu Vysledek</param>
        /// <returns></returns>
        private bool vysledekOVereniOK(XmlElement response, out string vysledek)
        {
            vysledek = response.SelectSingleNode("//*[local-name()='VysledekOvereni']")?.InnerText;

            return string.Equals(vysledek, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vrátí, zda volání NIA dopadlo úspšně
        /// </summary>
        /// <param name="response">Response obdržená z NIA</param>
        /// <param name="vysledek">Hodnota elementu Detail</param>
        /// <returns></returns>
        private bool responseStatusOK(XmlElement response, out String detail)
        {
            var status = response.SelectSingleNode("//*[local-name()='Status']")?.InnerText;

            detail = response.SelectSingleNode("//*[local-name()='Detail']")?.InnerText;
            
            return string.Equals(status, "OK", StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region Volání služeb NIA
        /// <summary>
        /// Zavolá službu NIA
        /// </summary>
        /// <param name="tclass">Metoda, která se má zavolat</param>
        /// <param name="bodyXml">Tělo požadavku</param>
        /// <returns>XML s výsledkem volání</returns>
        private XmlElement CallSubmissionService(string tclass, string bodyXml)
        {
            using (var factory = new ChannelFactory<IBusinessTransactions>("Token"))
            {
                var bodyPart = new BodyPart { Body = bodyXml.ToXmlElement() };
                var request = new SubmitRequest(tclass, new[] { bodyPart }, Array.Empty<OptionalParameter>());
                var response = factory.CreateChannelWithIssuedToken(GetActAsToken()).Submit(request);

                return Encoding.UTF8.GetString(Convert.FromBase64String(response.SubmitResult.BodyBase64XML)).ToXmlElement();
            }
        }

        /// <summary>
        /// Získá ActAs token 
        /// Pro jednoduchost se neřeší se jeho perzistence, ale pokaždé se lízne nový
        /// </summary>
        /// <returns>ActAs token </returns>
        private SecurityToken GetActAsToken()
        {
            GenericXmlSecurityToken actAsToken;
            var securityTokenHandlers = FederatedAuthentication.FederationConfiguration.IdentityConfiguration.SecurityTokenHandlers;

            using (var factory = new WSTrustChannelFactory("ActAs"))
            {
                factory.TrustVersion = TrustVersion.WSTrust13;

                var bootstrapContext = (BootstrapContext)((ClaimsIdentity)ClaimsPrincipal.Current.Identity).BootstrapContext;
                var rst = new RequestSecurityToken
                {
                    AppliesTo = new EndpointReference(MvcApplication.NiaConfiguration.RequestActAsTokenAppliesTo),
                    RequestType = RequestTypes.Issue,
                    KeyType = KeyTypes.Symmetric,
                    ActAs = bootstrapContext.SecurityToken != null
                        ? new SecurityTokenElement(bootstrapContext.SecurityToken)
                        : new SecurityTokenElement(bootstrapContext.Token.ToXmlElement(), securityTokenHandlers),
                    Claims = { new RequestClaim("http://eidas.europa.eu/attributes/naturalperson/PersonIdentifier") }
                };

                actAsToken = (GenericXmlSecurityToken)factory.CreateChannel().Issue(rst, out var rstr);
            }

            ViewBag.ActAsToken = actAsToken.TokenXml.ToPrettyString();

            return actAsToken;
        }
        #endregion
    }
}