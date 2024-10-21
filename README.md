# Prototyp pro autorizaci digitálního úkonu

Jedná se o pouze rychlý prototyp, který rozhodně není ukázkou, jak programovat 😉

Co autorizace digitáního úkonu, včetně možného způsobu implementace, je popsáno v [článku na Lupě](https://www.lupa.cz/clanky/jak-provadet-autorizaci-digitalniho-ukonu-u-online-podani/).

## Základní popis
ADU je zkratka pro **A**utorizaci **D**igitálního **Ú**konu.

Jedná se o ASP.NET MVC aplikaci v Microsoft NET Frameworku 4.8.1 (běží i pod 4.8).

Projekt je ve Visual Studiu 2022.

Po stažení je třeba spustit restore Nuget package (např. pravé tlačítko na Solution a "Restore NuGet Packages"). 

Aplikace využívá Windows Identity Foundation dostupný pro .NET Framework (NIA ho zřejmě podle vracených odpovědí používá také)
a ITfoxtec Identity SAML2 pro práci se SAML.

Prakticky veškerá logika je umístěna přímo v [src/Web/Controllers/NiaController.cs](src/Web/Controllers/NiaController.cs), 
zbytek je víceméně nutná omáčka pro přihlašování přes NIA a volání jejích služeb.

Aplikace je poměrně low-level, zobrazuje kompletní návratové response NIA apod.

Aplikace potřebuje přístup na internet pro volání služeb NIA na a ověřování certifikátů (to se dá v konfiguraci zakázat).

Aplikace potřebuje běžet s aplikačním poolem nastaveným na *"Load user profile = true"* (kvůli DPAPI). 

Na některých konfiguracích je třeba v konfiguraci aplikace v [src/Web/Web.config](src/Web/Web.config) v elementu **appSettings**:
~~~xml
<add key="Saml2:X509KeyStorageFlags" value="DefaultKeySet" />
~~~
nastavit hodnotu *MachineKeySet*. Problém je tom, že na těch konfiguracích odmítá pracovat s profilem uživatele, ale detailně jsme to nezkoumali. 

Popis, jak ADU funguje, je ve [vývojářské dokumentace NIA](https://dev.azure.com/SpravaZakladnichRegistru/NIA%20pro%20v%C3%BDvoj%C3%A1%C5%99e/_wiki/wikis/NIA-pro-v%C3%BDvoj%C3%A1%C5%99e.wiki/67/Autorizace-digit%C3%A1ln%C3%ADho-%C3%BAkonu)

## IIS
Projekt Visual Studia je zkonfigurován pro běh pod IIS jako https://localhost, ale neměl by být problém ho provozovat i
pod IIS Express (nezkoušeno). Při použití IIS je nutné pro debug Visual Studio spouštět jako administrátor.

Ve správě IIS je třeba vytvořit website směřující do [src/Web/](src/Web) a nabindovanou FQDN je třeba nastavit v [src/Web/Web.csproj](src/Web/Web.csproj) namísto localhost:
~~~xml
<WebProjectProperties>
    <IISUrl>https://localhost/</IISUrl>
</WebProjectProperties>
~~~
Pokud nebude FQDN uvedená v IISUrl dostupná, nepůjde projekt ve Visual Studiu otevřít.
Visual Studio může hlásit chybu o nedostupnosti site nebo že je projekt se stejným jménem již otevřen.

Pokud FQDN není v DNS, tak přidat záznam do C:\Windows\System32\drivers\etc\hosts
~~~
127.0.0.1 (FQDN)
~~~
## Sep
Pro testování musí být vytvořen v testovací NIA SeP, ve které bude aktivována autorizace digitálního úkonu. 

## Parametry SeP v NIA
Adresa vyplněná v poli "Unikátní URL adresa zabezpečené části vašeho webu" se musí shodovat s adresou uvedenou 
v konfiguraci aplikace v [src/Web/Web.config](src/Web/Web.config) v elementu **appSettings**:
~~~xml
<add key="Saml2:Issuer" value="https://(FQDN)/nia/" />
~~~

Aplikace pro přihlášení, odhlášení, a přihlášení pro provedení ADU podporuje následující endpointy, které musí být nastaveny v NIA v konfiguraci SeP:

### Přihlašování, odhlašování
**URL adresa pro příjem vydaného tokenu:**

https://(FQDN)/nia/process-login
 
**URL adresa, na kterou bude uživatel přesměrován po odhlášení z webu:**

https://(FQDN)/nia/process-logout

### Autorizace digitálního úkonu
**URL návratové adresy po autentizaci občana**

https://(FQDN)/nia/adu

V profilu SeP může být nastaveno více návratových url adres, na které má být uživatel v případě autorizace přihlášením, po jejím provedení,
přesměrován zpět k SeP. Na kterou z nich se má uživatel vrátit, se zasílá v rámci requestu, kterým se uživatel přesměruje do NIA.

Hodnotu je třeba nastavit v [src/Web/Web.config](src/Web/Web.config) v elementu **appSettings**:
~~~xml
<add key="Nia:ReturnUrl" value="https://(FQDN)/nia/adu" />
~~~

## Konfigurace aplikace
Konfigurace aplikace se provádí v souboru [src/Web/Web.config](src/Web/Web.config), parametry jsou okomentovány přímo v elementu **appSettings**.

### Certifikát pro SAML
V konfiguraci je na něj cesta v 
~~~xml
<add key="Saml2:CertificateFilePath" value="~/App_Data/certifikat_saml.pfx" />
~~~
Musíte mít vlastní a může se jednat o self-signed certifikát.

### Certifikát pro ADU
V konfiguraci je na něj cesta v 
~~~xml
<add key="Nia:AduCertificateFilePath" value="~/App_Data/certifikat_adu.pem" /> 
~~~
Musíte mít vlastní kvalifikovaný nebo komerční certifikát od jedné z CA:
- [I.CA](https://www.ica.cz/), 
- [Postsignum](https://postsignum.cz/) (testovací NIA akceptuje i [testovací Postsignum](https://postsignum.cz/testovaci_certifikat.html))
- ISZR AIS CA.
 
Co je obsahem certifikátu je jedno. Certifikát je (dle našich pokusů) unikátní pouze v rámci daného SeP, tzn. více SeP může používat
stejný certifikát (hodí se to v případě, pokud je provozováno více testovacích prostředí).
Certifikát musí být ve formátu PEM/CER (Base64 encoded X.509) a musí mít příponu CER, jinak ho NIA odmítne.

**K certifikátu nepotřebujete privátní klíč**, nic se pomocí něj nepodepisuje. Zřejmě měli autoři s certifikátem původně
jiný záměr, který se nerealizoval, nebo to mají nachystané na řešení do budoucna. 

Pokud není po ruce testovací certifikát, tak se odvážnější jedinci mohou pro účely testování porozhlédnout po internetu, kvalifikovaných
i komerčních certifikátů se na něm válejí tuny u vydávajících CA nebo v podepsaných PDF 🙂

## ActAs token
Pro volání služeb NIA souvisejících s autorizací digitálního úkonu je jako první třeba z NIA získat
[ActAs token](https://dev.azure.com/SpravaZakladnichRegistru/NIA%20pro%20v%C3%BDvoj%C3%A1%C5%99e/_wiki/wikis/NIA-pro-v%C3%BDvoj%C3%A1%C5%99e.wiki/69/ActAs-token).
Pro získání tokenu potřebujete do NIA předat SAML token, který obdržený z NIA při přihlášení uživatele, je třeba si ho uchovávat.

U získání ActAs může vzniknout situace, kdy se uživatel přihlásí přes NIA, bude vyplňovat formulář a pak ho chtít autorizovat.
Pokud od přihlášení uplyne 60 minut, tak vyprší platnost SAMLu vraceného NIA a získání ActAs tokenu zhavaruje.
V tomto případě je nutné uživatele nejprve dotlačit k novému přihlášení.

Získaný ActAs token má také platnost 60 minut.

## Volání služeb NIA
[Všechny služby NIA související s ADU](https://dev.azure.com/SpravaZakladnichRegistru/NIA%20pro%20v%C3%BDvoj%C3%A1%C5%99e/_wiki/wikis/NIA-pro-v%C3%BDvoj%C3%A1%C5%99e.wiki/67/Autorizace-digit%C3%A1ln%C3%ADho-%C3%BAkonu) 
(TR_ADU_START, TR_ADU_SEND_CODE, TR_ADU_CONFIRM_CODE, TR_ADU_STATUS) se vždy volají minimálně s parametry *Sepp* (Service Provider Pseudonym),
což je jen jiný název pro BSI přihlášeného uživatele, a *CertifikatHashBase64*, kde čeká několik pastí:
-  název je v dokumentaci uveden špatně, nejedná hash certifikátu, ale jedná se do base64 převedený obsah PEM/CER souboru s certifikátem,
-  do base64 se musí načítat a převádět **celý soubor** (včetně řádků „-----BEGIN CERTIFICATE----- a -----END CERTIFICATE----- ") který
   byl nahrán do NIA do konfigurace SeP pro autorizaci digitálního úkonu, nestačí jen posílat base64 část, která je uvedena v PEM/CER.
-  musí se jednat o **binárně shodný soubor** s tím, co je vložen do NIA. Pokud se do NIA nahraje soubor s CRLF a v rámci autorizace se bude
   zasílat sice významově úplně stejný soubor (stejný certifikát v PEM), ale např. s LF, tak to fungovat nebude. 
 
Hash souboru digitálního úkonu, který se předává do NIA, je SHA-256, v dokumentaci to není uvedeno.
