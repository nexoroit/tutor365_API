# Lesson content format

Lesson content lives in `content/lessons/<board>/<subjectcode>/<subtopic-code>.json` and is imported by the
`ContentSeeder` on start-up (idempotent: keys map to deterministic GUIDs, re-import updates in place).

All content must be ORIGINAL. The CGP / KS books are used only as an alignment reference (`CurriculumMappings`).
Never copy book text, question wording or diagrams.

## File

```json
{
  "subTopicCode": "CHE-03-02",
  "board": "AQA",
  "lessons": [ Lesson, ... ]
}
```

## Lesson

| field | type | notes |
|---|---|---|
| key | string | unique, e.g. `AQA-CHE-03-02-L1` |
| title | string | |
| summary | string | one or two sentences |
| objectives | string[] | 3–5 "I can…" statements |
| estimatedMinutes | int | 35–45 (one study slot) |
| difficulty | int 1–5 | |
| tier | `NotApplicable` / `Foundation` / `Higher` | default NotApplicable |
| activities | Activity[] | ordered; 8–14 items |
| practiceQuestions | Question[] | 6–12 extra questions used for re-attempts and reviews |

## Activity

```json
{ "type": "Explanation" | "Example" | "Question" | "Summary" | "Checkpoint" | "Reading",
  "title": "…", "content": "markdown (for non-question types)", "minutes": 4,
  "checkpoint": false, "question": Question }
```

A good lesson alternates: Explanation → Example → Question (quick check) → Explanation → … → exam-style Question → Summary.
Markdown may use headings, bold, lists, tables, LaTeX-style maths in `$…$`, and simple ASCII diagrams. No external images.

## Question

```json
{
  "key": "AQA-CHE-03-02-L1-Q3",
  "type": "MultipleChoice",
  "difficulty": 2,
  "marks": 1,
  "text": "markdown question stem",
  "hint": "a nudge that does not give the answer",
  "explanation": "worked solution shown after answering",
  "examStyle": false,
  "tier": "NotApplicable",
  "options":    [ { "text": "…", "correct": true, "feedback": "optional" } ],
  "answers":    [ { "text": "…", "caseSensitive": false, "numeric": 12.5, "tolerance": 0.1, "unit": "g", "blankIndex": 0, "marks": 1 } ],
  "markScheme": [ { "criterion": "…", "marks": 1, "keywords": [ ["biconcave", "concave"], ["surface area"] ] } ],
  "pairs":      [ { "left": "…", "right": "…" } ],
  "order":      [ "first", "second", "third" ],
  "metadata":   { "blanksText": "The ___ controls the cell.", "labels": ["A","B"], "unitsRequired": true }
}
```

Which sections are required per type:

| type | needs |
|---|---|
| MultipleChoice, TrueFalse | `options` (exactly one `correct`) |
| MultipleAnswer | `options` (one or more `correct`) |
| SingleAnswer, FillInTheBlank | `answers` (one per blank via `blankIndex`; use `metadata.blanksText` with `___`) |
| NumericalAnswer, FormulaCalculation, Equation | `answers` with `numeric` (+`tolerance`, `unit`) or `text` for symbolic answers |
| ShortAnswer, LongAnswer, ExamQuestion | `markScheme` (criteria with keyword groups; every group must match to award the criterion) |
| Matching, DragAndDrop, DiagramLabelling | `pairs` (left = item/label, right = match/position) |
| Ordering | `order` (correct sequence) |

Keyword groups are lowercase stems; matching is case-insensitive substring. Give 2–4 alternatives per group.
`marks` on the question must equal the total of the mark scheme / answers for multi-mark questions.
