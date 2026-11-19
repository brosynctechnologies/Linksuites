# API contract — Online Course Platform (LearnLink)

Same API and conventions as the other products: camelCase JSON, string enums, money in minor units, ProblemDetails,
`402` + `code: "plan_limit"`. Instants are ISO 8601 with offset; dates `YYYY-MM-DD`.

Two sides share one course catalogue:
- **Academy (tutor SaaS):** each tutor business is a tenant with its own academy page `/{web}/a/{academySlug}`.
- **Marketplace:** courses a tutor lists *and* an admin approves appear in the shared catalogue.
All student payments go to the platform's own account with the provider chosen in the admin console's Payment settings (Paystack or OPay); tutors earn the price minus a fee and are paid out. Each order records the provider that took it (`provider`), and the return check, callbacks, the background re-check and refunds always use that provider, so orders taken before a switch keep working.

## Accounts and tokens

| Who | Sign up / log in | Token |
|---|---|---|
| Tutor (academy owner/team) | `/api/auth/signup` `{ fullName, email, password, country, product: "courses", businessName }`; login/otp with `product: "courses"` | tenant token (`tenant.role` Owner/Admin/Staff/Accountant) |
| Learner | `POST /api/auth/learner/signup` `{ fullName, email, password }`; `POST /api/auth/learner/login` `{ email, password }`; refresh with `/api/auth/refresh` | learner token: `AuthResponse.tenant` is **null** |

One email can be both (tutors can take courses). Learner endpoints (`/api/learn/...`) accept any signed-in token and act as that user.
`AuthResponse.user` gains `isPlatformAdmin` (true for the platform's admin emails) for the admin area.

## Plans (tutors, `/api/billing`)

| Plan | Price/month | Courses | Video storage | Fee on academy sales | Coupons, certificates | Branding | Team seats |
|---|---|---|---|---|---|---|---|
| Free | ₦0 | 2 | 2 GB | 10% | — | shown | 1 |
| Pro | ₦15,000 | unlimited | 50 GB | 0% | ✓ | removed | 3 |
| Business | ₦40,000 | unlimited | 250 GB | 0% | ✓ | removed | 10 |

Marketplace sales always carry the **marketplace fee: 30%** (the platform found the learner).
Limit keys: `courses`, `video_storage_mb`, `academy_fee_pct`, `coupons`, `certificates`, `remove_branding`, `team_seats`.
Earnings become available **7 days** after the sale (refund window).

## Academy settings (tutor)

| Method | Path | Body / returns |
|---|---|---|
| GET / PUT | `/api/academy` | `AcademyDto` / `UpdateAcademyRequest` |
| POST | `/api/academy/logo` | multipart `file` → `AcademyDto` |
| PUT | `/api/academy/payout-account` | `{ bankName, accountNumber, accountName }` → `AcademyDto` |

`AcademyDto = { name, slug, headline, bio, logoUrl, brandColor, currency, payoutAccount: { bankName, accountNumber, accountName } | null, academyUrl }`
`UpdateAcademyRequest = { name, slug, headline, bio, brandColor }`

## Studio: courses, curriculum, lessons (tutor)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/studio/courses` | | `StudioCourseSummaryDto[]` |
| POST | `/api/studio/courses` | `SaveCourseRequest` | `StudioCourseDto` (402 when the plan's course limit is reached) |
| GET / PUT / DELETE | `/api/studio/courses/{id}` | `SaveCourseRequest` | `StudioCourseDto` (delete only with no enrolments; otherwise archive) |
| POST | `/api/studio/courses/{id}/thumbnail` | multipart `file` | `StudioCourseDto` |
| POST | `/api/studio/courses/{id}/publish` / `/unpublish` | | `StudioCourseDto` (publish needs ≥1 lesson) |
| POST | `/api/studio/courses/{id}/submit-to-marketplace` | | `StudioCourseDto` (status becomes `InReview`) |
| POST | `/api/studio/courses/{id}/sections` | `{ title }` | `StudioCourseDto` |
| PUT / DELETE | `/api/studio/sections/{id}` | `{ title }` | `StudioCourseDto` |
| POST | `/api/studio/sections/{id}/lessons` | `{ title, type }` | `StudioCourseDto` |
| PUT | `/api/studio/courses/{id}/order` | `{ sections: [{ id, lessonIds: [] }] }` (full order, lessons may move between sections) | `StudioCourseDto` |
| GET / PUT / DELETE | `/api/studio/lessons/{id}` | `SaveLessonRequest` | `StudioLessonDto` |
| POST | `/api/studio/lessons/{id}/video` | multipart `file` (mp4/mov/webm/mkv, ≤ 2 GB) | `StudioLessonDto` (video `Processing`) |
| GET | `/api/studio/lessons/{id}/video` | | `VideoDto` (poll while Processing) |
| POST / DELETE `{resourceId}` | `/api/studio/lessons/{id}/resources` | multipart `file` + `title` field (≤ 50 MB) | `StudioLessonDto` |
| PUT | `/api/studio/lessons/{id}/quiz` | `SaveQuizRequest` | `StudioLessonDto` |

`SaveCourseRequest = { title, subtitle, description, categoryId, level, language, priceMinor, isFree, outcomes: string[], requirements: string[], listOnMarketplace }`
`level`: `Beginner`, `Intermediate`, `Advanced`, `AllLevels`.
`StudioCourseSummaryDto = { id, title, slug, thumbnailUrl, status, marketplaceStatus, priceMinor, isFree, lessonsCount, studentsCount, ratingAverage, ratingCount, revenueMinor, updatedAt }`
`StudioCourseDto = SaveCourseRequest & { id, slug, thumbnailUrl, status, marketplaceStatus, marketplaceNote, publishedAt, courseUrl, sections: [{ id, title, position, lessons: [LessonSummaryDto] }] }`
`status`: `Draft`, `Published`, `Archived`. `marketplaceStatus`: `NotListed`, `InReview`, `Approved`, `Rejected` (`marketplaceNote` explains a rejection).
`LessonSummaryDto = { id, title, type, position, isPreview, durationSec, videoStatus }`
Lesson `type`: `Video`, `Text`, `File`, `Quiz`. `videoStatus`: `None`, `Processing`, `Ready`, `Failed`.
`SaveLessonRequest = { title, type, isPreview, content }` (`content` = Markdown body for text lessons and notes under videos)
`StudioLessonDto = SaveLessonRequest & { id, sectionId, courseId, durationSec, video: VideoDto | null, resources: [{ id, title, fileName, sizeBytes, url }], quiz: QuizDto | null }`
`VideoDto = { status, format, durationSec, thumbnailUrl, error, renditions: string[] }` (`format`: `hls` or `mp4`; `renditions` like `["240p","480p","720p"]`)
`SaveQuizRequest = { timeLimitMinutes, passMarkPercent, maxAttempts, shuffle, questions: [{ id, type, prompt, points, explanation, options: [{ id, text, isCorrect }], acceptedAnswers: string[] }] }`
Question `type`: `SingleChoice`, `MultipleChoice`, `TrueFalse`, `ShortAnswer`. `timeLimitMinutes`/`maxAttempts` null = none. Short answers match `acceptedAnswers` case-insensitively, ignoring extra spaces.
`QuizDto` = the same as `SaveQuizRequest` with ids filled in.

## Studio: learners, Q&A, reviews, coupons, money (tutor)

| Method | Path | Returns |
|---|---|---|
| GET | `/api/studio/dashboard` | `{ currency, studentsTotal, enrolmentsThisMonth, revenueThisMonthMinor, availableMinor, pendingMinor, ratingAverage, recentEnrolments: [{ studentName, courseTitle, enrolledAt, amountMinor, channel (null when free) }] }` |
| GET | `/api/studio/students?courseId=&q=&page=` | `Paged<{ enrolmentId, userId, name, email, courseId, courseTitle, enrolledAt, progressPercent, completedAt, source }>` |
| GET | `/api/studio/questions?courseId=&unanswered=` | `QuestionDto[]` |
| POST | `/api/studio/questions/{id}/answers` | `{ body }` → `QuestionDto` |
| GET | `/api/studio/reviews?courseId=` | `[{ id, courseTitle, studentName, rating, comment, createdAt }]` |
| GET / POST, PUT / DELETE `{id}` | `/api/studio/coupons` | `CouponDto` (Pro+) |
| GET | `/api/studio/earnings?page=` | `{ currency, availableMinor, pendingMinor, paidOutMinor, entries: Paged<{ id, orderId, courseTitle, studentName, channel, grossMinor, feeMinor, netMinor, availableAt, createdAt, status }> }` |
| GET | `/api/studio/payouts` | `PayoutDto[]` |
| POST | `/api/studio/payouts` | `{ amountMinor }` → `PayoutDto` (needs a payout account; up to `availableMinor`; minimum ₦5,000) |
| POST | `/api/studio/orders/{id}/refund` | → 204 (within 7 days of purchase; removes access and the earning). Refunded through the provider that took the payment (Paystack or OPay); the order is claimed first, so a double click refunds once, and a refund the provider refuses answers 503 and leaves the order Paid. |

`CouponDto = { id, code, courseId, discountType, value, maxUses, uses, expiresAt, isActive }` (`courseId` null = all courses; `discountType` `Percent` or `Fixed`)
`PayoutDto = { id, amountMinor, status, bankName, accountNumber, accountName, reference, requestedAt, paidAt, note }`; `status`: `Requested`, `Paid`, `Rejected`.
Earning `status`: `Pending` (in the 7-day window), `Available`, `PartlyAvailable` (part of it is in a payout, the rest can be requested), `Requested` (wholly in a payout request not yet paid), `PaidOut`, `Reversed`. Paid payouts cover the oldest earnings first, then requested ones. `channel`: `Marketplace` or `Academy`.
`enrolmentsThisMonth` and `revenueThisMonthMinor` count from midnight on the 1st in Africa/Lagos; refunded (revoked) enrolments are not counted. A learner who re-enrols after a refund is counted in the course's student count again.
`QuestionDto = { id, courseId, courseTitle, lessonId, lessonTitle, authorName, body, createdAt, answers: [{ id, authorName, isTutor, body, createdAt }] }`

## Public catalogue (no sign-in)

| Method | Path | Returns |
|---|---|---|
| GET | `/api/catalog/categories` | `[{ id, name, slug, parentId }]` |
| GET | `/api/catalog/courses?q=&category=&level=&price=free|paid&sort=popular|newest|rating|price&page=&pageSize=` | `Paged<CourseCardDto>` (marketplace-approved, published courses) |
| GET | `/api/catalog/academies/{academySlug}` | `{ name, slug, headline, bio, logoUrl, brandColor, showBranding, courses: CourseCardDto[] }` (all of the academy's published courses) |
| GET | `/api/catalog/courses/{academySlug}/{courseSlug}` | `CourseDetailDto` |
| GET | `/api/catalog/courses/{academySlug}/{courseSlug}/reviews?page=` | `Paged<{ studentName, rating, comment, createdAt }>` |
| GET | `/api/catalog/lessons/{lessonId}/preview` | `LessonPlaybackDto` for lessons marked `isPreview` |
| GET | `/api/certificates/{code}` | `{ code, studentName, courseTitle, tutorName, academyName, issuedAt }` (verify page) |
| GET | `/api/certificates/{code}/pdf` | PDF |

`CourseCardDto = { id, slug, academySlug, academyName, title, subtitle, thumbnailUrl, priceMinor, currency, isFree, level, ratingAverage, ratingCount, studentsCount, lessonsCount, durationSec, categoryName }`
`CourseDetailDto = CourseCardDto & { description, language, outcomes, requirements, updatedAt, tutor: { name, headline, bio, logoUrl }, brandColor, sections: [{ id, title, lessons: [{ id, title, type, durationSec, isPreview }] }], enrolled }`
(`enrolled` is true when a learner token is sent and they're enrolled.)

## Buying and learning (signed-in learner)

| Method | Path | Body | Returns |
|---|---|---|---|
| POST | `/api/learn/checkout` | `{ courseId, couponCode, channel }` | `{ enrolled, authorizationUrl, orderId, priceMinor, discountMinor, totalMinor }` |
| POST | `/api/learn/checkout/quote` | `{ courseId, couponCode }` | `{ priceMinor, discountMinor, totalMinor, couponMessage }` |
| POST | `/api/learn/orders/{orderId}/verify` | `{ reference }` | `{ enrolled, courseUrl }` |
| GET | `/api/learn/enrollments` | | `[MyCourseDto]` |
| GET | `/api/learn/courses/{courseId}` | | `LearnCourseDto` (403 if not enrolled) |
| GET | `/api/learn/lessons/{lessonId}` | | `LessonPlaybackDto` |
| PUT | `/api/learn/lessons/{lessonId}/progress` | `{ positionSec, completed, mediaDurationSec? }` | `{ progressPercent, courseCompleted, certificateCode }`; a video lesson the server couldn't measure learns its length from `mediaDurationSec` (the length the player read), never from `positionSec` |
| GET / PUT | `/api/learn/lessons/{lessonId}/note` | `{ body }` | `{ body, updatedAt }` |
| GET / POST | `/api/learn/lessons/{lessonId}/questions` | `{ body }` | `QuestionDto[]` / `QuestionDto` |
| POST | `/api/learn/questions/{id}/answers` | `{ body }` | `QuestionDto` |
| POST | `/api/learn/quizzes/{lessonId}/attempts` | | `{ attemptId, startedAt, endsAt, questions: [{ id, type, prompt, points, options: [{ id, text }] }] }` (no answers) |
| POST | `/api/learn/attempts/{attemptId}/submit` | `{ answers: [{ questionId, optionIds, text }] }` | `QuizResultDto` |
| GET | `/api/learn/quizzes/{lessonId}/attempts` | | `QuizResultDto[]` |
| POST / PUT | `/api/learn/courses/{courseId}/review` | `{ rating, comment }` | `{ rating, comment }` |
| GET | `/api/learn/certificates` | | `[{ code, courseTitle, issuedAt, pdfUrl, verifyUrl }]` |

`channel`: `Marketplace` (default) or `Academy` (send `Academy` when the learner came from the tutor's academy page).
Free courses and 100% coupons enrol at once (`enrolled: true`, no `authorizationUrl`). Otherwise go to `authorizationUrl`;
the checkout returns the learner to `{web}/checkout/{orderId}?reference=...` (Paystack adds the reference itself; for OPay the API puts it in the return URL, and a learner who cancels on OPay comes back with `&cancelled=1`), which calls `/verify`. `/verify` asks the order's provider (status, amount and currency must match; a wrong amount marks the order Failed). Checkout answers 503 while online payment is off in Payment settings, or when the chosen provider has no keys on the server.
`MyCourseDto = { courseId, slug, academySlug, title, thumbnailUrl, tutorName, progressPercent, lessonsDone, lessonsTotal, lastLessonId, completedAt, certificateCode }`
`LearnCourseDto = { courseId, title, academySlug, slug, tutorName, progressPercent, certificateCode, brandColor, sections: [{ id, title, lessons: [{ id, title, type, durationSec, completed, isQuiz, passed }] }], resumeLessonId }`
`LessonPlaybackDto = { id, courseId, title, type, content, durationSec, video: { status, format, url, thumbnailUrl } | null, resources: [{ title, url, sizeBytes }], quiz: { questionsCount, timeLimitMinutes, passMarkPercent, maxAttempts, attemptsUsed, bestScorePercent, passed } | null, positionSec, completed, previousLessonId, nextLessonId, watermark }`
`video.url` is a short-lived signed link (valid ~2 hours): an `.m3u8` playlist when `format` is `hls` (play with hls.js; Safari plays it natively), or an `.mp4` file otherwise. `watermark` is the learner's email: overlay it faintly on the player.
`QuizResultDto = { attemptId, scorePercent, passed, submittedAt, questions: [{ id, prompt, correct, explanation, yourOptionIds, yourText, correctOptionIds }] }` (correct answers are only shown after a submitted attempt).
A course is complete when every lesson is completed and every quiz passed; a certificate is issued then (Pro+ tutors), `certificateCode` is set.

## Platform admin (users with `isPlatformAdmin`)

| Method | Path | Body | Returns |
|---|---|---|---|
| GET | `/api/admin/course-reviews` | | `[{ courseId, title, academyName, submittedAt, lessonsCount, priceMinor, courseUrl }]` |
| POST | `/api/admin/course-reviews/{courseId}` | `{ approve, note }` | 204 |
| GET | `/api/admin/payouts?status=Requested` | | `[PayoutDto & { academyName, product, businessName, currency }]` (shared queue: courses and property payouts) |
| POST | `/api/admin/payouts/{id}/paid` | `{ reference }` | `PayoutDto` |
| POST | `/api/admin/payouts/{id}/reject` | `{ note }` | `PayoutDto` (the money returns to the tutor's available balance) |
| GET / POST, PUT `{id}` | `/api/admin/categories` | `{ name, slug, parentId }` | category |
| GET | `/api/admin/overview` | | `{ academies, courses, learners, salesThisMonthMinor, platformFeesThisMonthMinor, pendingReviews, pendingPayouts }` |

Webhooks: Paystack course payments arrive on the existing `POST /api/webhooks/paystack` (references start `crs_`, signed). OPay callbacks arrive on `POST /api/webhooks/opay`, which reads only the reference and asks the order's provider (at most every 30 seconds per order); the body is never trusted. Orders whose callback never came are re-checked every 10 minutes for 48 hours (with plan payments, `Jobs:BillingEnabled`).
