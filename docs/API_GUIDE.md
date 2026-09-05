# Tutor365 API guide for the frontend

Base URL (dev): `http://localhost:5293/api/v1` · Swagger: `http://localhost:5293/swagger` (full request/response schemas).

## Envelope
Every response: `{ "success": bool, "data": T | null, "message": string|null, "errorCode": string|null, "errors": { field: [msg] } | null, "traceId": string }`.
HTTP status still carries meaning: 400 validation, 401 unauthenticated/expired (`errorCode` `TOKEN_EXPIRED` → call refresh), 403 forbidden, 404 not found, 409 conflict, 422 business rule (`LESSON_LOCKED`, `SESSION_INCOMPLETE`, `INVALID_OTP`, `ANSWER_REQUIRED` …), 500 internal.
Enums are strings. Dates are ISO 8601 UTC. IDs are GUIDs.

## Roles
`Student`, `Parent`, `Admin` (no tutor role: the platform is the tutor). The JWT carries `role` and `profileId` (studentId or parentId). Never let the user pick a role; route on `user.role` from the login response.

## Auth
| Endpoint | Notes |
|---|---|
| `POST /auth/register` `{email,password,firstName,lastName,phone?}` | Parent only. Sends 6-digit OTP. |
| `POST /auth/verify-email` `{email,code}` | Returns tokens on success. |
| `POST /auth/resend-otp` `{email,purpose:"Registration"|"PasswordReset"}` | |
| `POST /auth/login` `{email,password,rememberMe}` | All roles. `EMAIL_NOT_VERIFIED` (401) means show the OTP screen. |
| `POST /auth/refresh` `{refreshToken}` | Rotating: store the new pair, discard the old. |
| `POST /auth/logout` `{refreshToken}` | |
| `POST /auth/forgot-password` `{email}` → `POST /auth/reset-password` `{email,code,newPassword}` | Works for students and parents. |
| `POST /auth/change-password`, `GET /auth/me` | `me` returns `student`/`parent` profile blocks. |

Access token: 60 min. Refresh: 30 days (90 with rememberMe). Send `Authorization: Bearer <accessToken>`.

## Public reference data
`GET /exam-boards`, `GET /year-groups`, `GET /subjects` (anonymous). Authenticated: `GET /subjects/{id}/tree`, `/topics?subjectId&year`, `/subtopics?topicId`, `/lessons?subjectId|topicId|subTopicId`, `/lessons/{id}`.

## Parent
- `GET /parents/me/children` → cards (overall %, minutes this week, streak). `POST /parents/me/children` creates a child (`yearGroupId` from `/year-groups`, `targetGrade` 1–9, optional `sessionsPerDay`/`sessionMinutes`).
- `GET|PUT /parents/me/children/{id}` · `POST …/password` · `POST …/active?isActive=`
- `GET|PUT …/schedule` `{sessionsPerDay, sessionMinutes, activeDays:["Monday",…], autoPlanEnabled}` (the 2×45 / 3×45 setting)
- `GET|PUT …/subjects` per-subject `{targetGrade, passThresholdPercent, maxAttemptsBeforeMoveOn, tier, isEnabled, priority}` — `passThresholdPercent` is the grade expectation that gates the next lesson.
- Timetable: `GET …/timetable` (slots per subject this week and why: year, exam date, lessons left, grade gap, priority), `GET …/calendar?from&to`, `POST …/week/regenerate`. Changing the schedule or subject priorities rebuilds upcoming planned slots automatically.
- Views: `…/dashboard`, `…/progress`, `…/progress/subjects`, `…/progress/topics?subjectId`, `…/topics/{topicId}/lessons`, `…/sessions`, `…/mistakes`, `…/today`, `…/week`, `…/recommendations`, `…/reports/weekly?weekStart=`
- Assigned work: `POST|GET …/study-plans`, `DELETE /study-plans/{id}`, `DELETE /study-plans/{id}/items/{itemId}`
- `GET /notifications?unreadOnly=&page=&pageSize=`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all`

## Student
- `GET /students/me/dashboard` → greeting, today's plan (`today.slots[]` with `status` Scheduled/InProgress/Completed/Missed/Skipped), `continueSession`, `recommended`, `subjects[]`, `recentResults[]`, `weakAreas[]`, `assignedWork[]`, `unreadNotifications`.
- `GET /students/me/today?date=`, `GET /students/me/week`, `GET /students/me/calendar?from&to` (max 62 days, future days auto-generated), `GET /students/me/timetable?weekStart` (the app-built weekly subject allocation with rationale), `POST /students/me/week/regenerate`, `POST /students/me/today/regenerate`, `POST /students/me/today/slots/{id}/skip`
- `GET /students/me/recommendations`, `/progress`, `/progress/subjects`, `/progress/topics?subjectId`, `/topics/{topicId}/lessons` (status Locked/Available/InProgress/Completed/Passed), `/results`, `/sessions?status=`, `/mistakes?subjectId=`, `/assigned-work`, `/study-plans`, `/reports/weekly`

## Study session (core loop)
1. `POST /study-sessions` with one of: `{dailyStudySlotId}` (from today's plan), `{lessonId}`, `{subjectId}` / `{topicId}` (next recommended lesson), or `{type:"Review", topicId, questionCount}`. Returns the full `StudySessionDto`. If the same lesson already has an Active/Paused session it is resumed (pass `forceNew:true` to restart).
2. Render `activities[]` in order. `currentActivity` is where the student is. Types: Explanation, Example, Reading, Summary (show `contentMarkdown`), Question (show `question`).
3. Questions: `POST /study-sessions/{id}/answers` `{questionId, answerText?, answerJson?, timeSpentSeconds, hintUsed}`. Response has `correct, score, maxScore, feedback, missingCriteria[], explanation, correctAnswer, nextAction` (`Continue` | `ReviewOrContinue` | `Complete`) and updated `session` progress. First attempt scores; further attempts to the same question are practice (feedback only).
4. `POST /study-sessions/{id}/navigate` `{direction:"next"|"previous"|"goto", activityId?}` — `next` past an unanswered question returns 422 `ANSWER_REQUIRED`.
5. Auto-save: `PATCH /study-sessions/{id}` `{elapsedSeconds, currentActivityId, clientState}` every 30–60 s and on pause/exit.
6. `POST …/pause`, `POST …/resume`, `POST …/complete` (422 `SESSION_INCOMPLETE` if questions remain; `?force=true` submits as-is), `POST …/abandon`, `GET …/result`, `GET /study-sessions/active` (for "Continue previous session?"), `GET …/questions/{qid}/hint`.
7. Result: `scorePercent`, `passed` vs `passThresholdPercent`, `attemptNumber`, `canRetry`, `nextLessonUnlocked`, `nextLessonId`, `performance[]` bands, `mistakes[]`, `message`. If not passed, start the same `lessonId` again → attempt 2 (alternate questions are swapped in).

### Answer formats by `questionType`
| type | `question` fields to render | send |
|---|---|---|
| MultipleChoice, TrueFalse | `options[] {id,text}` | `answerJson:{selectedOptionIds:[id]}` |
| MultipleAnswer | `options[]` | `answerJson:{selectedOptionIds:[...]}` |
| SingleAnswer | text input | `answerText` |
| FillInTheBlank | `blanksText` with `___`, `blankCount` | `answerJson:{blanks:["…","…"]}` |
| NumericalAnswer, FormulaCalculation, Equation | text/number input (units accepted) | `answerText:"0.2 mol"` |
| ShortAnswer, LongAnswer, ExamQuestion | textarea (`maxMarks` shown) | `answerText` |
| Matching, DragAndDrop, DiagramLabelling | `options[]` (left items) + `matchTargets[]` (shuffled) | `answerJson:{pairs:[{optionId,matchKey:"<target text>"}]}` |
| Ordering | `options[]` (shuffled) | `answerJson:{order:[optionId,…]}` |

`hasHint` tells you whether to show the hint button. Never rely on the client to hide answers: the API never sends correct flags before an answer.

## AI tutor (server-side, stub until a provider is configured)
`POST /ai-tutor/hint|explain|example|why-wrong|message` `{sessionId, questionId?, message?, conversationId?}` → `{conversationId, reply (markdown), isStub, suggestedActions[]}`. Keep `conversationId` for follow-ups.

## Admin
`GET /admin/dashboard`, `GET|POST /admin/users`, `GET|PUT|DELETE /admin/users/{id}`, `POST /admin/users/{id}/password`, `GET /admin/audit-logs`, `GET|PUT /admin/system-settings[/{key}]`,
`GET /admin/curriculum?subjectId`, `POST|PUT /admin/topics[/{id}]`, `POST|PUT /admin/subtopics[/{id}]`, `GET|POST|PUT /admin/lessons[/{id}]`, `PUT /admin/lessons/{id}/activities`, `GET|POST|PUT /admin/questions[/{id}]`, `POST /admin/{topic|subtopic|lesson|question}/{id}/status?status=Published|Draft|Archived`, `POST /admin/content/import`.

## Paging
List endpoints take `page`, `pageSize` and return `{items, page, pageSize, totalCount, totalPages, hasNext, hasPrevious}`.
