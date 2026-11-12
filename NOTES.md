# Task 1 notes: approach and limitations

## Approach

The program never looks a field up by a fixed id, name, class, XPath or CSS selector. It looks at every candidate element on the page, gives each a score for "how much does this look like the field I want", and uses the best one.

- **Evidence used:** the element's label (`<label for>`, a wrapping `<label>`, `aria-labelledby`, `aria-label`, `title`), its placeholder, and the nearest short piece of text before it (so a plain `<div>` caption works). The `id` and `name` attributes only add a small bonus.
- **First Name:** "first/given/fore name" scores up. "Last/sur/family name" scores heavily down, so a Last Name box is never picked.
- **Gender:** the radio's own text (or aria-label, or value) must equal the wanted option as a whole word, so "Male" never matches "Female". A radio inside a small group that mentions gender gets a bonus.
- **State:** every `<select>` is scored by its label text. "State" and "province" score up. "Country", "city" and "district" score down.
- **Safety nets:** each step is retried up to 3 times for slow pages. If State has no options until a Country is chosen, the program fills Country and waits.
- **Honest result:** after every action it reads the value back (typed text, radio selected, dropdown selected) and prints PASS or FAIL. The exit code is 0 only if all three pass.
- **Configurable:** the URL comes from `--url`, then `FORM_URL`, then the default CloudQA page.

## What was tested

| Page | Result |
|---|---|
| Live CloudQA form | all three PASS |
| `modified-form.html` (reordered, new ids/names/classes, decoys) | all three PASS |
| `variants/variant-no-labels.html` (no `<label>`, text only in `aria-label`) | all three PASS |
| `variants/variant-wrappers-decoys.html` (extra wrapper divs, decoy fields) | all three PASS |

Testing the no-label page found a real bug: radio buttons that only had an `aria-label` were ignored, so Gender failed. It is fixed (radio `aria-label`, `aria-labelledby` and `title` are now read).

## Limitations

- Only native `<select>` dropdowns. Custom div-based dropdowns are not handled.
- Nothing inside iframes or shadow DOM is searched.
- English labels only. Gender synonyms are limited to Male/Man/M and Female/Woman/F.
- The caption must be within 3 levels of nesting above the field. A caption separated from its field by more wrapper levels than that will be missed.
- If two candidates tie, the first one on the page wins. For example, a second radio group titled "Spouse Gender" placed before the real Gender group would tie and be chosen.
- A field with no label, no placeholder, no aria-label and no recognisable id/name cannot be identified.
- The Country-then-State step is a best guess and has not been checked against a real dependent dropdown.
- On the live page the State dropdown lists country-style names, so the first real option is chosen unless `--state` is given.
- Chrome only.
