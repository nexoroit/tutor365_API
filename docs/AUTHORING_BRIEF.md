# Lesson authoring brief (for content authors)

You are writing ORIGINAL GCSE teaching content for Tutor365, a UK GCSE tutoring platform, aligned to the AQA specification.
Audience: students in Years 9–11 (ages 13–16). Tone: friendly, clear, encouraging, like a good one-to-one tutor. British spelling.

Read `docs/CONTENT_FORMAT.md` (the JSON schema) and `content/lessons/AQA/CHE/CHE-03-02.json` (a complete worked sample) before writing.
The sub-topic codes, names and spec references are in `src/Tutor365.Infrastructure/Data/Seed/Curriculum/aqa-*.json`.

## Rules
1. ORIGINAL content only. Never copy CGP, KS3/KS4 textbooks, BBC Bitesize, past papers or any other source. Write your own explanations, examples and questions.
2. One file per sub-topic: `content/lessons/AQA/<SUBJECT>/<SUBTOPIC-CODE>.json` containing ONE lesson (key `AQA-<SUBTOPIC-CODE>-L1`) that covers the whole sub-topic in 35–45 minutes.
3. Each lesson: 10–14 activities alternating Explanation → Example → Question, ending with an exam-style checkpoint Question and a Summary. Include at least 5 Question activities of mixed types.
4. `practiceQuestions`: 8–12 extra questions, mixed types, spread across difficulty 1–4, at least two `ExamQuestion`/`ShortAnswer` with mark schemes.
5. Use a variety of question types: MultipleChoice, TrueFalse, MultipleAnswer, FillInTheBlank, NumericalAnswer, ShortAnswer, ExamQuestion, Matching, Ordering. For maths use NumericalAnswer/FormulaCalculation heavily. For English use ShortAnswer/LongAnswer/ExamQuestion with rich mark schemes and provide short original extracts (written by you) inside the question text when a text is needed.
6. Mark schemes: 1 mark per criterion, each criterion has keyword groups (arrays of lowercase alternatives). Every group must be present for the criterion to be awarded, so keep to 1–2 groups per criterion and 2–5 alternatives per group.
7. Numerical answers: give `tolerance` sensibly (0 for integers, ~1% for decimals), and `unit` where relevant.
8. Question `marks` must equal the sum of answer/mark-scheme marks. Difficulty 1–5. Every question needs `hint` and `explanation`.
9. Content is markdown. Use `$…$` for maths where helpful (e.g. `$x^2$`), tables, bullet lists. Keep explanations 120–250 words each. No external images or links.
10. Keys must be unique: `AQA-<SUBTOPIC>-L1-Q<n>` for activity questions and `AQA-<SUBTOPIC>-L1-P<n>` for practice questions.
11. The file must be valid JSON (no trailing commas, escape quotes and newlines). Validate every file with:
    `python3 -c "import json,sys;json.load(open(sys.argv[1]))" <file>`
12. Do not modify any other files. Do not run git commands.
