using System.Collections.Generic;

namespace ComicVineAI
{
	/// <summary>Imprint to parent publisher table of the Python scraper (cvimprints.py), copied as is.</summary>
	public static class ImprintMap
	{
		private const string Actionlab = "Action Lab";
		private const string Amryl = "Amryl Entertainment";
		private const string Ape = "Ape Entertainment";
		private const string Avatar = "Avatar Press";
		private const string Boom = "Boom!";
		private const string Darkhorse = "Dark Horse Comics";
		private const string Dc = "DC Comics";
		private const string Dynamite = "Dynamite Entertainment";
		private const string Hakusensha = "Hakusensha";
		private const string Heroic = "Heroic Publishing";
		private const string Idw = "IDW Publishing";
		private const string Image = "Image";
		private const string Kodansha = "Kodansha";
		private const string Lion = "Lion Forge Comics";
		private const string Malibu = "Malibu";
		private const string Marvel = "Marvel";
		private const string Nbm = "Nbm";
		private const string Penguin = "Penguin Group";
		private const string Radio = "Radio Comix";
		private const string Slg = "Slg Publishing";
		private const string Titan = "Titan Comics";
		private const string Tokuma = "Tokuma Shoten";
		private const string Tokyopop = "Tokyopop";
		private const string Wizard = "Wizard";

		private static readonly Dictionary<string, string> map = new Dictionary<string, string>
		{
			["2000AD"] = Dc,
			["Adventure"] = Malibu,
			["Aircel Publishing"] = Malibu,
			["America's Best Comics"] = Dc,
			["Amerotica "] = Nbm,
			["Antimatter"] = Amryl,
			["Apparat"] = Avatar,
			["Archaia"] = Boom,
			["Berger Books"] = Darkhorse,
			["BOOM! Box"] = Boom,
			["Boundless Comics"] = Avatar,
			["Black Bull"] = Wizard,
			["Black Crown"] = Idw,
			["Blu Manga"] = Tokyopop,
			["CMX"] = Dc,
			["Chaos! Comics"] = Dynamite,
			["Cliffhanger"] = Dc,
			["Comic Bom Bom"] = Kodansha,
			["ComicsLit"] = Nbm,
			["Curtis Magazines"] = Marvel,
			["Danger Zone"] = Actionlab,
			["Dark Horse Books"] = Darkhorse,
			["Dark Horse Manga"] = Darkhorse,
			["Desperado Publishing"] = Image,
			["Epic"] = Marvel,
			["Eternity"] = Malibu,
			["Eurotica "] = Nbm,
			["Focus"] = Dc,
			["Helix"] = Dc,
			["Hero Comics"] = Heroic,
			["Homage comics"] = Dc,
			["Hudson Street Press"] = Penguin,
			["Icon Comics"] = Marvel,
			["Impact"] = Dc,
			["Jets Comics"] = Hakusensha,
			["KaBOOM!"] = Boom,
			["KiZoic"] = Ape,
			["Kodansha Comics Digital-First!"] = Kodansha,
			["Kodansha Comics USA"] = Kodansha,
			["MAD"] = Dc,
			["Marvel Digital Comics Unlimited"] = Marvel,
			["Marvel Knights"] = Marvel,
			["Marvel Music"] = Marvel,
			["Marvel Soleil"] = Marvel,
			["Marvel UK"] = Marvel,
			["Maverick"] = Darkhorse,
			["Max"] = Marvel,
			["Milestone"] = Dc,
			["Minx"] = Dc,
			["Papercutz"] = Nbm,
			["Paradox Press"] = Dc,
			["Piranha Press"] = Dc,
			["Quillion"] = Lion,
			["Razorline"] = Marvel,
			["Roar Comics"] = Lion,
			["ShadowLine"] = Image,
			["Silverline"] = Image,
			["Sin Factory Comix"] = Radio,
			["Skybound"] = Image,
			["Slave Labor"] = Slg,
			["Star Comics"] = Marvel,
			["Tangent Comics"] = Dc,
			["Titan Books"] = Titan,
			["Todd McFarlane Productions"] = Image,
			["Tokuma Comics"] = Tokuma,
			["Top Cow"] = Image,
			["Top Shelf"] = Idw,
			["Ultraverse"] = Malibu,
			["Vertical"] = Kodansha,
			["Vertigo"] = Dc,
			["Wildstorm"] = Dc,
			["Zuda Comics"] = Dc,
		};

		/// <summary>Exact lookup of the trimmed name, like the scraper. Returns the same text when it is not a known imprint.</summary>
		public static string FindParentPublisher(string imprint)
		{
			string parent = imprint;
			if (imprint != null && map.TryGetValue(imprint.Trim(), out string found))
			{
				parent = found;
			}
			return parent;
		}
	}
}
