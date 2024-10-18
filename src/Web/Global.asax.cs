using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using System.Web.Helpers;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

using AutorizaceDigitalnihoUkonu.Web.Lib;
using System.Web.Hosting;

namespace AutorizaceDigitalnihoUkonu.Web
{
    public class MvcApplication : HttpApplication
    {
        public static Saml2Configuration Saml2Configuration { get; } = new Saml2Configuration();
        public static NiaConfiguration NiaConfiguration { get; } = new NiaConfiguration();

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            AntiForgeryConfig.UniqueClaimTypeIdentifier = ClaimTypes.NameIdentifier;

            // FilterConfig
            {
                GlobalFilters.Filters.Add(new HandleErrorAttribute());
            }

            // RouteConfig
            {
                RouteTable.Routes.LowercaseUrls = true;
                RouteTable.Routes.AppendTrailingSlash = false;
                RouteTable.Routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
                RouteTable.Routes.MapMvcAttributeRoutes();
                RouteTable.Routes.MapRoute(
                    name: "Default",
                    url: "{controller}/{action}",
                    defaults: new { controller = "Home", action = "Index" }
                );
            }

            // BundleConfig
            {
                BundleTable.Bundles.Add(new StyleBundle("~/bundles/css").Include("~/Content/bootstrap.css", "~/Content/site.css"));
            }

            // Saml2Config
            {
                // Pokud načtení certifikátu končí na chybu "X509Certificate2: CryptographicException: The system cannot find the file specified." a soubor existuje a proces k němu má přístup, tak je třeba buď v aplikačním poolu nastavit "Load user profile = true" nebo v konfiguraci nastavit hodnotu "MachineKeySet"
                var certificate = new X509Certificate2(HostingEnvironment.MapPath(ConfigurationManager.AppSettings["Saml2:CertificateFilePath"]), ConfigurationManager.AppSettings["Saml2:CertificatePassword"], (X509KeyStorageFlags)Enum.Parse(typeof(X509KeyStorageFlags), ConfigurationManager.AppSettings["Saml2:X509KeyStorageFlags"]));

                Saml2Configuration.Issuer = ConfigurationManager.AppSettings["Saml2:Issuer"];
                Saml2Configuration.DecryptionCertificates.Add(certificate);
                Saml2Configuration.SigningCertificate = certificate;
                Saml2Configuration.SignAuthnRequest = true;
                Saml2Configuration.AllowedAudienceUris.Add(Saml2Configuration.Issuer);
                Saml2Configuration.SaveBootstrapContext = true;

                if (String.Equals(ConfigurationManager.AppSettings["Saml2:ValidateCertificate"], "false", StringComparison.OrdinalIgnoreCase))
                {
                    Saml2Configuration.CertificateValidationMode = System.ServiceModel.Security.X509CertificateValidationMode.None;
                }


                using (var httpClient = new HttpClient())
                {
                    var entityDescriptor = new EntityDescriptor()
                        .ReadIdPSsoDescriptorFromUrlAsync(httpClient, new Uri(ConfigurationManager.AppSettings["Saml2:IdPMetadataUrl"]))
                        .GetAwaiter()
                        .GetResult();

                    if (entityDescriptor.IdPSsoDescriptor != null)
                    {
                        Saml2Configuration.AllowedIssuer = entityDescriptor.EntityId;
                        Saml2Configuration.SingleSignOnDestination = entityDescriptor.IdPSsoDescriptor.SingleSignOnServices
                            .First(x => x.Binding.OriginalString == "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect")
                            .Location;
                        Saml2Configuration.SingleLogoutDestination = entityDescriptor.IdPSsoDescriptor.SingleLogoutServices
                            .First(x => x.Binding.OriginalString == "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect")
                            .Location;

                        entityDescriptor.IdPSsoDescriptor.SigningCertificates.Where(c=> c.IsValidLocalTime()).ToList().ForEach(c => Saml2Configuration.SignatureValidationCertificates.Add(c));
                    }
                }

                if (Saml2Configuration.SignatureValidationCertificates.Count <= 0)
                {
                    throw new Exception("Chybí podpisový certifikát pro identity providera.");
                }
            }

            // NiaConfig
            {
                var aduCertificateFilePath = HostingEnvironment.MapPath(ConfigurationManager.AppSettings["Nia:AduCertificateFilePath"]);
                if (!File.Exists(aduCertificateFilePath))
                {
                    throw new Exception("Chybí certifikát pro autorizaci digitálního úkonu.");
                }
                NiaConfiguration.RequestActAsTokenAppliesTo = ConfigurationManager.AppSettings["Nia:RequestActAsTokenAppliesTo"];
                NiaConfiguration.AduCertificateBase64 = Convert.ToBase64String(File.ReadAllBytes(aduCertificateFilePath));
                NiaConfiguration.AduAutorizacePodaniUrl = ConfigurationManager.AppSettings["Nia:AduAutorizacePodaniUrl"];
            }
        }
    }
}