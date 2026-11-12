using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;

namespace SelfHealingForm
{
    class Program
    {
        const string DefaultUrl = "https://app.cloudqa.io/home/AutomationPracticeForm";

        // JavaScript that collects every piece of human-readable text that describes an element:
        // <label for>, wrapping <label>, aria-labelledby, and the nearest short text before it.
        const string LabelScript = @"
            var el = arguments[0];
            var parts = [];
            function add(t) { if (t && t.trim()) { parts.push(t.trim()); } }
            if (el.id) {
                var labels = document.querySelectorAll('label');
                for (var i = 0; i < labels.length; i++) {
                    if (labels[i].htmlFor === el.id) { add(labels[i].innerText); }
                }
            }
            var wrap = el.closest('label');
            if (wrap) { add(wrap.innerText); }
            var ids = el.getAttribute('aria-labelledby');
            if (ids) {
                var arr = ids.split(' ');
                for (var j = 0; j < arr.length; j++) {
                    var n = document.getElementById(arr[j]);
                    if (n) { add(n.innerText); }
                }
            }
            var node = el;
            for (var level = 0; level < 3 && node; level++) {
                var prev = node.previousElementSibling;
                if (prev && prev.innerText && prev.innerText.length < 60 && !prev.querySelector('input,select,textarea')) {
                    add(prev.innerText);
                    break;
                }
                node = node.parentElement;
            }
            return parts.join(' | ');";

        // Label text for a radio button: its own label, or the text right after it.
        const string RadioLabelScript = @"
            var el = arguments[0];
            var t = '';
            if (el.id) {
                var labels = document.querySelectorAll('label');
                for (var i = 0; i < labels.length; i++) {
                    if (labels[i].htmlFor === el.id) { t = labels[i].innerText; }
                }
            }
            if (!t) { var w = el.closest('label'); if (w) { t = w.innerText; } }
            if (!t) { t = el.getAttribute('aria-label') || ''; }
            if (!t) {
                var ids = el.getAttribute('aria-labelledby');
                if (ids) {
                    var arr = ids.split(' ');
                    for (var j = 0; j < arr.length; j++) {
                        var ref = document.getElementById(arr[j]);
                        if (ref) { t += ' ' + ref.innerText; }
                    }
                }
            }
            if (!t.trim()) { t = el.getAttribute('title') || ''; }
            if (!t.trim()) {
                var n = el.nextSibling;
                while (n && !t.trim()) {
                    if (n.nodeType === 3) { t = n.textContent; }
                    else if (n.nodeType === 1) { t = n.innerText; }
                    n = n.nextSibling;
                }
            }
            if (!t) { t = el.value || ''; }
            return t.trim();";

        // True if the radio sits inside a small group whose text mentions gender/sex.
        const string GenderGroupScript = @"
            var node = arguments[0].parentElement;
            for (var level = 0; level < 5 && node; level++) {
                var count = node.querySelectorAll('input[type=radio]').length;
                var groupText = (node.innerText || '') + ' ' + (node.getAttribute('aria-label') || '') + ' ' + (node.getAttribute('title') || '');
                if (count <= 5 && /gender|sex/i.test(groupText)) { return true; }
                node = node.parentElement;
            }
            return false;";

        static int Main(string[] args)
        {
            string url = GetSetting(args, "--url", "FORM_URL", DefaultUrl);
            string firstName = GetSetting(args, "--first-name", "FIRST_NAME", "Harsh");
            string gender = GetSetting(args, "--gender", "GENDER", "Male");
            string state = GetSetting(args, "--state", "STATE_NAME", "");
            string country = GetSetting(args, "--country", "COUNTRY", "India");
            bool headless = GetSetting(args, "--headless", "HEADLESS", "false").ToLower() == "true";
            bool keepOpen = GetSetting(args, "--keep-open", "KEEP_OPEN", "false").ToLower() == "true";

            Console.WriteLine("URL: " + url);

            ChromeOptions options = new ChromeOptions();
            if (headless)
            {
                options.AddArgument("--headless=new");
            }
            options.AddArgument("--window-size=1400,1000");
            // Keep the console clean: hide Chrome's own log noise (e.g. updater errors on Windows).
            options.AddExcludedArgument("enable-logging");
            options.AddArgument("--log-level=3");

            IWebDriver driver = new ChromeDriver(options);
            int failures = 0;

            try
            {
                driver.Navigate().GoToUrl(url);
                WaitForPage(driver);

                failures += RunStep("First Name", delegate { return FillFirstName(driver, firstName); });
                failures += RunStep("Gender", delegate { return ChooseGender(driver, gender); });
                failures += RunStep("State", delegate { return ChooseState(driver, state, country); });
            }
            catch (Exception ex)
            {
                Console.WriteLine("FATAL: " + ex.Message);
                failures++;
            }
            finally
            {
                if (keepOpen)
                {
                    Console.WriteLine("Browser left open. Press Enter here to close it...");
                    Console.ReadLine();
                }
                driver.Quit();
            }

            Console.WriteLine(failures == 0 ? "OVERALL: PASS" : "OVERALL: FAIL (" + failures + " step(s) failed)");
            return failures == 0 ? 0 : 1;
        }

        delegate string Step();

        // A field may not exist yet on a slow or script-built page, so each step is tried up to 3 times.
        static int RunStep(string name, Step step)
        {
            string lastError = "";
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string message = step();
                    Console.WriteLine("PASS  " + name + " -> " + message);
                    return 0;
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                    if (attempt < 3) { System.Threading.Thread.Sleep(1500); }
                }
            }
            Console.WriteLine("FAIL  " + name + " -> " + lastError);
            return 1;
        }

        // ---------- First Name ----------

        static string FillFirstName(IWebDriver driver, string value)
        {
            IWebElement best = null;
            int bestScore = 0;
            string bestInfo = "";

            IList<IWebElement> inputs = driver.FindElements(By.TagName("input"));
            foreach (IWebElement input in inputs)
            {
                string type = (input.GetAttribute("type") ?? "text").ToLower();
                if (type != "text" && type != "search" && type != "")
                {
                    continue;
                }
                if (!input.Displayed || !input.Enabled)
                {
                    continue;
                }

                string label = GetText(driver, LabelScript, input);
                string placeholder = input.GetAttribute("placeholder");
                string aria = input.GetAttribute("aria-label");
                string name = input.GetAttribute("name");
                string id = input.GetAttribute("id");

                int score = ScoreFirstName(label, placeholder, aria, name, id);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = input;
                    bestInfo = "label='" + label + "' placeholder='" + placeholder + "'";
                }
            }

            if (best == null)
            {
                throw new Exception("could not find a field that looks like First Name");
            }

            best.Clear();
            best.SendKeys(value);

            string actual = best.GetAttribute("value");
            if (actual != value)
            {
                throw new Exception("typed '" + value + "' but field contains '" + actual + "'");
            }
            return "typed '" + value + "' (matched by " + bestInfo + ", score " + bestScore + ")";
        }

        static int ScoreFirstName(string label, string placeholder, string aria, string name, string id)
        {
            int score = 0;

            string[] humanTexts = new string[] { label, placeholder, aria };
            foreach (string text in humanTexts)
            {
                string n = Normalize(text);
                if (Regex.IsMatch(n, @"\b(first|given|fore) ?name\b")) { score += 10; }
                else if (n == "name") { score += 2; }
                if (Regex.IsMatch(n, @"\b(last|sur|family) ?name\b")) { score -= 20; }
            }

            string[] techTexts = new string[] { name, id };
            foreach (string text in techTexts)
            {
                string compact = Normalize(text).Replace(" ", "");
                if (compact.Contains("firstname") || compact.Contains("fname") || compact.Contains("givenname")) { score += 5; }
                if (compact.Contains("lastname") || compact.Contains("lname") || compact.Contains("surname")) { score -= 20; }
            }

            return score;
        }

        // ---------- Gender ----------

        static string ChooseGender(IWebDriver driver, string wanted)
        {
            string wantedNorm = Normalize(wanted);
            string[] wantedForms = GenderForms(wantedNorm);
            IWebElement best = null;
            int bestScore = 0;
            string bestLabel = "";

            IList<IWebElement> radios = driver.FindElements(By.CssSelector("input[type='radio']"));
            foreach (IWebElement radio in radios)
            {
                string label = GetText(driver, RadioLabelScript, radio);
                string value = radio.GetAttribute("value");

                int score = 0;
                string labelNorm = Normalize(label);
                string valueNorm = Normalize(value);
                // Whole-text comparison, so "Male" never matches "Female". Common synonyms count too (Man, Woman, M, F).
                if (Array.IndexOf(wantedForms, labelNorm) >= 0) { score += 10; }
                else if (Array.IndexOf(wantedForms, valueNorm) >= 0) { score += 8; }
                // Label that starts with the wanted word, such as "Male (M)". "Female" never starts with "male".
                else if (Regex.IsMatch(labelNorm, "^" + Regex.Escape(wantedNorm) + @"\b")) { score += 6; }

                if (score == 0) { continue; }

                bool inGenderGroup = (bool)((IJavaScriptExecutor)driver).ExecuteScript(GenderGroupScript, radio);
                if (inGenderGroup) { score += 5; }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = radio;
                    bestLabel = label;
                }
            }

            if (best == null)
            {
                throw new Exception("could not find a radio button for '" + wanted + "'");
            }

            ClickRadio(driver, best);

            if (!best.Selected)
            {
                throw new Exception("clicked '" + wanted + "' but it is not selected");
            }
            return "selected '" + bestLabel + "' (score " + bestScore + ")";
        }

        static string[] GenderForms(string wantedNorm)
        {
            if (wantedNorm == "male" || wantedNorm == "man" || wantedNorm == "m") { return new string[] { "male", "man", "m" }; }
            if (wantedNorm == "female" || wantedNorm == "woman" || wantedNorm == "f") { return new string[] { "female", "woman", "f" }; }
            return new string[] { wantedNorm };
        }

        static void ClickRadio(IWebDriver driver, IWebElement radio)
        {
            try
            {
                radio.Click();
            }
            catch (WebDriverException)
            {
                // Custom-styled radios are often hidden or covered by a label. Fall back to JavaScript.
                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", radio);
            }
        }

        // ---------- State ----------

        static bool IsRealOption(IWebElement option)
        {
            string optionValue = option.GetAttribute("value");
            string optionText = option.Text.Trim().ToLower();
            return optionValue != "" && optionText != "" && !optionText.StartsWith("select") && !optionText.StartsWith("-");
        }

        static int CountRealOptions(IWebElement select)
        {
            int count = 0;
            SelectElement dropdown = new SelectElement(select);
            foreach (IWebElement option in dropdown.Options)
            {
                if (IsRealOption(option)) { count++; }
            }
            return count;
        }

        // All the human-readable text that describes a dropdown: its label, aria-label and title. Only when
        // none of those exist is its first option used (often a placeholder such as "Select State"). The
        // first option is a last resort because it can be misleading: on the CloudQA page the State
        // dropdown starts with "-- Select Country --".
        static string SelectText(IWebDriver driver, IWebElement select)
        {
            string text = GetText(driver, LabelScript, select) + " " + select.GetAttribute("aria-label") + " " + select.GetAttribute("title");
            if (Normalize(text) == "")
            {
                IList<IWebElement> options = select.FindElements(By.TagName("option"));
                if (options.Count > 0) { text = options[0].Text; }
            }
            return text;
        }

        // Index of the option whose text matches (ignoring case, spacing and punctuation), or -1.
        static int FindOptionIndex(IWebElement select, string wanted)
        {
            string w = Normalize(wanted);
            IList<IWebElement> options = new SelectElement(select).Options;
            for (int i = 0; i < options.Count; i++)
            {
                if (Normalize(options[i].Text) == w) { return i; }
            }
            return -1;
        }

        // Quick scan: the select on the page that looks most like "State".
        static IWebElement FindStateSelect(IWebDriver driver)
        {
            IWebElement best = null;
            int bestScore = 0;
            IList<IWebElement> selects = driver.FindElements(By.TagName("select"));
            foreach (IWebElement select in selects)
            {
                if (!select.Displayed) { continue; }
                int score = ScoreState(SelectText(driver, select), select.GetAttribute("name"), select.GetAttribute("id"));
                if (score > bestScore)
                {
                    bestScore = score;
                    best = select;
                }
            }
            return best;
        }

        // Fills the Country field (a text box or a dropdown) if the page has one. Returns true if it did.
        static bool TrySetCountry(IWebDriver driver, string country)
        {
            IList<IWebElement> candidates = driver.FindElements(By.CssSelector("input, select"));
            foreach (IWebElement candidate in candidates)
            {
                if (!candidate.Displayed || !candidate.Enabled) { continue; }
                if (candidate.GetAttribute("type") == "radio" || candidate.GetAttribute("type") == "checkbox") { continue; }

                string label = Normalize(GetText(driver, LabelScript, candidate)) + " " + Normalize(candidate.GetAttribute("placeholder"));
                if (!Regex.IsMatch(label, @"\bcountry\b")) { continue; }

                try
                {
                    if (candidate.TagName.ToLower() == "select")
                    {
                        SelectElement countrySelect = new SelectElement(candidate);
                        int index = FindOptionIndex(candidate, country);
                        if (index < 0) { return false; }
                        countrySelect.SelectByIndex(index);
                    }
                    else
                    {
                        candidate.Clear();
                        candidate.SendKeys(country);
                        candidate.SendKeys(Keys.Tab);
                    }
                    return true;
                }
                catch (WebDriverException)
                {
                    return false;
                }
            }
            return false;
        }

        // Some forms keep State empty until a Country is chosen. If State has no real options,
        // fill the Country field and wait for the options to appear.
        static void PrepareCountryIfNeeded(IWebDriver driver, string country)
        {
            IWebElement stateSelect = FindStateSelect(driver);
            if (stateSelect == null || CountRealOptions(stateSelect) > 0)
            {
                return;
            }

            TrySetCountry(driver, country);

            // Wait up to 5 seconds for State options to load. Search again each time to avoid stale elements.
            for (int i = 0; i < 10; i++)
            {
                System.Threading.Thread.Sleep(500);
                IWebElement fresh = FindStateSelect(driver);
                if (fresh != null && CountRealOptions(fresh) > 0) { return; }
            }
            throw new Exception("State dropdown has no options even after entering Country '" + country + "'");
        }

        // If a specific State was asked for but the dropdown does not list it, the page may fill State
        // only after a Country is chosen. Enter the Country and wait up to 5 seconds for the option to appear.
        static void PrepareCountryForState(IWebDriver driver, string wanted, string country)
        {
            if (wanted == "") { return; }
            IWebElement stateSelect = FindStateSelect(driver);
            if (stateSelect == null || FindOptionIndex(stateSelect, wanted) >= 0) { return; }

            if (!TrySetCountry(driver, country)) { return; }

            for (int i = 0; i < 10; i++)
            {
                System.Threading.Thread.Sleep(500);
                IWebElement fresh = FindStateSelect(driver);
                if (fresh != null && FindOptionIndex(fresh, wanted) >= 0) { return; }
            }
        }

        static string ChooseState(IWebDriver driver, string wanted, string country)
        {
            PrepareCountryIfNeeded(driver, country);
            PrepareCountryForState(driver, wanted, country);

            IWebElement best = null;
            int bestScore = 0;
            string bestLabel = "";

            IList<IWebElement> selects = driver.FindElements(By.TagName("select"));
            foreach (IWebElement select in selects)
            {
                if (!select.Displayed || !select.Enabled) { continue; }

                string label = GetText(driver, LabelScript, select);
                string name = select.GetAttribute("name");
                string id = select.GetAttribute("id");

                int score = ScoreState(SelectText(driver, select), name, id);
                if (wanted != "" && FindOptionIndex(select, wanted) >= 0) { score += 4; }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = select;
                    bestLabel = label;
                }
            }

            if (best == null)
            {
                throw new Exception("could not find a dropdown that looks like State");
            }

            SelectElement dropdown = new SelectElement(best);

            if (wanted != "")
            {
                int index = FindOptionIndex(best, wanted);
                if (index < 0)
                {
                    throw new Exception("option '" + wanted + "' is not in the dropdown. Some available options: " + ListOptions(dropdown));
                }
                dropdown.SelectByIndex(index);
            }
            else
            {
                // No state given: pick the first real option (skip placeholders like "Select...").
                bool picked = false;
                IList<IWebElement> allOptions = dropdown.Options;
                for (int i = 0; i < allOptions.Count; i++)
                {
                    if (IsRealOption(allOptions[i]))
                    {
                        dropdown.SelectByIndex(i);
                        picked = true;
                        break;
                    }
                }
                if (!picked) { throw new Exception("dropdown has no real options"); }
            }

            string chosen = dropdown.SelectedOption.Text.Trim();
            if (wanted != "" && Normalize(chosen) != Normalize(wanted))
            {
                throw new Exception("wanted '" + wanted + "' but dropdown shows '" + chosen + "'");
            }
            return "selected '" + chosen + "' (matched by label='" + bestLabel + "', score " + bestScore + ")";
        }

        static int ScoreState(string label, string name, string id)
        {
            int score = 0;
            string n = Normalize(label);
            if (Regex.IsMatch(n, @"\bstate\b") || Regex.IsMatch(n, @"\bprovince\b")) { score += 10; }
            if (Regex.IsMatch(n, @"\b(country|city|district|pin|zip)\b")) { score -= 10; }

            string[] techTexts = new string[] { name, id };
            foreach (string text in techTexts)
            {
                string t = Normalize(text);
                if (Regex.IsMatch(t, @"\bstate\b")) { score += 5; }
                if (Regex.IsMatch(t, @"\b(country|city)\b")) { score -= 10; }
            }
            return score;
        }

        static string ListOptions(SelectElement dropdown)
        {
            List<string> names = new List<string>();
            foreach (IWebElement option in dropdown.Options)
            {
                if (IsRealOption(option))
                {
                    names.Add(option.Text.Trim());
                    if (names.Count == 8) { break; }
                }
            }
            return string.Join(", ", names);
        }

        // ---------- Helpers ----------

        static string GetText(IWebDriver driver, string script, IWebElement element)
        {
            object result = ((IJavaScriptExecutor)driver).ExecuteScript(script, element);
            return result == null ? "" : result.ToString();
        }

        static string Normalize(string text)
        {
            if (text == null) { return ""; }
            text = Regex.Replace(text, "([a-z])([A-Z])", "$1 $2");
            text = text.ToLower();
            text = Regex.Replace(text, "[^a-z0-9]+", " ");
            return text.Trim();
        }

        static void WaitForPage(IWebDriver driver)
        {
            WebDriverWait wait = new WebDriverWait(driver, TimeSpan.FromSeconds(20));
            wait.Until(delegate (IWebDriver d)
            {
                string ready = ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState").ToString();
                return ready == "complete" && d.FindElements(By.TagName("input")).Count > 0;
            });
        }

        // Order of priority: command-line argument, then environment variable, then default.
        static string GetSetting(string[] args, string argName, string envName, string defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == argName) { return args[i + 1]; }
            }
            string fromEnv = Environment.GetEnvironmentVariable(envName);
            if (!string.IsNullOrEmpty(fromEnv)) { return fromEnv; }
            return defaultValue;
        }
    }
}
