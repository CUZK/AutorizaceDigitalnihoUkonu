using System.Xml.Linq;

namespace AutorizaceDigitalnihoUkonu.Web.Lib
{
    /// <summary>
    /// Add eIDAS extensions to the SAML request.
    /// </summary>
    public class EidasExtensions : ITfoxtec.Identity.Saml2.Schemas.Extensions
    {
        private static readonly string EidasNamespace = "http://eidas.europa.eu/saml-extensions";
        private static readonly XNamespace EidasNamespaceX = XNamespace.Get(EidasNamespace);

        public EidasExtensions()
        {
            Element.Add(
                new XAttribute(XNamespace.Xmlns + "eidas", EidasNamespace),
                new XElement(EidasNamespaceX + "SPType", "public"),
                new XElement(
                    EidasNamespaceX + "RequestedAttributes",
                    GetRequestedAttribute("http://eidas.europa.eu/attributes/naturalperson/PersonIdentifier"),
                    GetRequestedAttribute("http://eidas.europa.eu/attributes/naturalperson/CurrentGivenName"),
                    GetRequestedAttribute("http://eidas.europa.eu/attributes/naturalperson/CurrentFamilyName")
                )
            );
        }

        private static XElement GetRequestedAttribute(string name, bool isRequired = false, string value = null)
        {
            var element = new XElement(
                EidasNamespaceX + "RequestedAttribute",
                new XAttribute("Name", name),
                new XAttribute("NameFormat", "urn:oasis:names:tc:SAML:2.0:attrname-format:uri"),
                new XAttribute("isRequired", isRequired)
            );

            if (!string.IsNullOrWhiteSpace(value))
            {
                element.Add(new XElement(EidasNamespaceX + "AttributeValue", value));
            }

            return element;
        }
    }
}