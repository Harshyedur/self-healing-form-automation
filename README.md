# Self-Healing Form Automation (C# + Selenium)

Automates three fields on the CloudQA Automation Practice Form: **First Name** (text box), **Gender** (radio button) and **State** (dropdown).

The program does not rely on a fixed id, name, class, XPath or CSS selector. It finds each field by what it *means* to a user (its label, placeholder, aria-label, nearby text and, for dropdowns, the placeholder option), so it keeps working when the page changes.

See also `NOTES.md` for a short note on the approach and its limitations.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (check with `dotnet --version`)
- Google Chrome (ChromeDriver is downloaded automatically by Selenium Manager)

## Setup

```
git clone <this-repo-url>
cd <repo-folder>
dotnet restore
```

## Run

On the default page:

```
dotnet run
```

On any other version of the page (the URL is configurable):

```
dotnet run -- --url "https://your-modified-page"
```

To leave the browser open at the end (useful for demos):

```
dotnet run -- --keep-open true
```

The URL priority is: command-line argument, then the `FORM_URL` environment variable, then the built-in default.

### Options

| Option | Env variable | Default |
|---|---|---|
| `--url` | `FORM_URL` | `https://app.cloudqa.io/home/AutomationPracticeForm` |
| `--first-name` | `FIRST_NAME` | Harsh |
| `--gender` | `GENDER` | Male |
| `--state` | `STATE_NAME` | the first real option in the dropdown |
| `--country` | `COUNTRY` | India (only used if State has no options until a Country is entered) |
| `--keep-open true` | `KEEP_OPEN` | false (waits for Enter before closing the browser) |
| `--headless true` | `HEADLESS` | false |

If you pass `--state` with a value that is not in the dropdown, the program fails and lists some of the real options. The match ignores upper/lower case and extra spaces, so `maharashtra` finds "Maharashtra".

### Output

One line per field, then an overall result:

```
PASS  First Name -> typed 'Harsh' (matched by label='First Name | First Name' placeholder='Name', score 22)
PASS  Gender -> selected 'Male' (score 15)
PASS  State -> selected 'Afganistan' (matched by label='State | State', score 20)
OVERALL: PASS
```

The exit code is 0 when everything passes and 1 otherwise. Each field is verified after the action: the typed text is read back, the radio's selected state is checked, and the dropdown's selected option is read.

## Testing against a changed page

Three test pages are included: `modified-form.html` and the two files in `variants/`.

`modified-form.html` is included. It is a scrambled version of the form with:

- fields in a different order
- all ids, names and classes changed
- decoys: a "Last Name" box, a "Nickname" box, a "Country" dropdown placed before State, and another Yes/No radio group

Run it (PowerShell, from the project folder):

```
dotnet run -- --keep-open true --state Maharashtra --url "file:///$((Resolve-Path .\modified-form.html).Path -replace '\\','/')"
```

On macOS or Linux:

```
dotnet run -- --keep-open true --state Maharashtra --url "file://$PWD/modified-form.html"
```

Two more test pages are in `variants/`:

- `variants/variant-no-labels.html`: no `<label>` tags at all. Text lives only in `aria-label`.
- `variants/variant-wrappers-decoys.html`: extra wrapper divs around every field, plus decoys ("Company Name", "Preferred Language", "Spouse Gender").

```
dotnet run -- --state Maharashtra --url "file://$PWD/variants/variant-no-labels.html"
dotnet run -- --state Maharashtra --url "file://$PWD/variants/variant-wrappers-decoys.html"
```

See `NOTES.md` for the approach and limitations.

## Approach

Every candidate element on the page gets a score, and the best match wins. Nothing depends on where an element is on the page.

- **Label discovery:** for each element the program collects its `<label for>`, a wrapping `<label>`, `aria-labelledby`, `aria-label`, `placeholder`, and the nearest short text just before it. The `name` and `id` attributes are used only as extra evidence, never as the main way to find a field.
- **First Name:** matches "first name", "given name", "forename", "fname" and similar. Anything that looks like "last name" or "surname" is heavily penalised, so a similar-looking Last Name field is never chosen. A field labelled only "Name" is a weak last resort.
- **Gender:** among all radio buttons, the label (its own label, `aria-label`, `aria-labelledby`, `title`, the text right after it, or finally its value) must match the wanted option as a whole, so "Male" never matches "Female". Common synonyms are accepted (Male = Man = M, Female = Woman = F), and a label that starts with the wanted word, such as "Male (M)", also matches. A bonus applies when the radio sits inside a small group whose text or `aria-label` mentions gender. If a radio is hidden or covered by styling, the program falls back to a JavaScript click.
- **State:** among all `<select>` elements, the program reads each dropdown's label, aria-label and title. Only if it has none of those does it use the dropdown's first option (for example "Select State"), because a first option can mislead: on the CloudQA page the State dropdown starts with "-- Select Country --". Text matching "state" or "province" scores highest. "Country", "city", "district" and "zip" are penalised, and a dropdown that contains the requested option gets a bonus. If the State dropdown has no real options, or does not list the requested `--state`, the program fills the Country field (`--country`, default India) and waits up to 5 seconds for the option to appear.
- **Slow pages:** each field step is tried up to 3 times, 1.5 seconds apart, in case the field is added to the page late.
- **Verification:** after acting, the program checks the result and reports FAIL honestly if it does not match.
- Because matching is score-based and does not depend on position, reordering fields or changing attributes does not break it.

## Limitations

- Only native `<select>` dropdowns are supported. Custom (div-based) dropdowns are not handled.
- Fields inside iframes or shadow DOM are not searched.
- Only English labels are matched. A label with very different wording may not be recognised. Gender synonyms are limited to Male/Man/M and Female/Woman/F.
- The Country-then-State step is a best guess for pages where State depends on Country. It has not been confirmed against a real dependent dropdown, and it assumes the Country field is a text box or a dropdown.
- The caption must be within 3 levels of nesting above the field. A caption separated from its field by more wrapper levels is missed.
- If two elements score the same, the first one on the page wins. For example, a "Spouse Gender" radio group placed before the real Gender group would tie and be chosen.
- A dropdown with no label, no aria-label and no recognisable placeholder option or id/name cannot be identified as State.
- Only Chrome is supported.
- On the live CloudQA page, the State dropdown currently lists country-style names such as "Afganistan", so the default (first real option) is used. A state such as "Maharashtra" is only selectable on pages whose dropdown contains it.
