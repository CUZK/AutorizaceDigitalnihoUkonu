using System.Web.Mvc;

namespace AutorizaceDigitalnihoUkonu.Web.Controllers
{
	public class HomeController : Controller
	{
		[HttpGet]
		public ActionResult Index()
		{
			return View();
		}
	}
}