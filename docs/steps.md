# Steps

Steps in the sample data, and why. The adult Discipleship Path is built. The Student Path is still a plan: it waits for attendance per person, so a student can come to the student service on their own rhythm. The decisions are 83 to 89 and D-steps-8 to D-steps-10 in [Decisions](decisions.md). The numbers live in `configuration/steps.json`, so they can be tuned in one place.

## The idea

The sample church has two step programs. The adult program shows the seven things the church hopes every adult will do. The student program is a shorter version of the same path for Jr High and High School.

Both programs follow [Ministry by Design](https://electric-eustoma-cf2.notion.site/Ministry-by-Design-325c7b9c0987809188aefa868c2da771), a framework for how a church moves people forward. Its order is:

1. **Belong.** People are known and connected before they are asked to do anything.
2. **Equip.** People grow as followers of Jesus.
3. **Contribute.** People take part in the church's mission.
4. **Lead.** People are trusted with influence and help others grow.

Ministry by Design puts growth before serving on purpose. Responsibility without formation creates pressure. It also says the path is "the norm, not a gate": people can take steps out of order.

Published church pathways agree on the same spine: commit, belong, grow, serve, and multiply. Research on discipleship also warns that activity alone does not show growth. So every step in the sample church comes from a real record, never from a random count.

## What Rock ships

A new Rock database already has two step programs:

- **Discipleship Path,** with four step types: Baptism, Starting Point Class, Serve, and Small Group. Serve requires Starting Point Class first. Baptism, Serve, and Small Group are system step types with fixed Guids.
- **Core Steps,** a system program that holds eRA. Rock's Calculate Family Analytics job writes it.

The build follows the serving pattern (decision 68): it keeps Rock's program, its step types, and their Guids, and builds them out. Any Rock feature that looks them up by Guid still works.

## Adult program: Discipleship Path

Seven steps, in the Ministry by Design order. The completion flow is Rock's **Linear (Preferred)**: the order is a suggestion, and Rock still shows and enforces the one prerequisite (Serve after Starting Point Class).

| # | Step | Stage | Kind | Objective | Impact | Where it comes from |
|---|---|---|---|---|---|---|
| 1 | Starting Point Class (Rock's) | Belong | Milestone | Outreach | 2 | A class the person took, before their first group or team |
| 2 | Small Group (Rock's) | Belong | Rhythm | Discipleship | 4 | Each small group membership |
| 3 | Baptism (Rock's) | Equip | Milestone | Discipleship | 5 | The person's Baptism Date |
| 4 | Foundations (new) | Equip | Milestone | Discipleship | 3 | A six-week course on what Christians believe, reading the Bible, and prayer |
| 5 | Serve (Rock's) | Contribute | Rhythm | Activation | 4 | Each serving team membership |
| 6 | Give (new) | Contribute | Rhythm | Activation | 3 | The giving journey: from Consistent to Lapsed |
| 7 | Lead (new) | Lead | Rhythm | Activation | 5 | Each membership in a leader role, on a small group or a serving team |

- **Kind** is Rock's engagement type. A milestone happens once. A rhythm is a habit that starts and can stop.
- **Objective** is Rock's organizational objective: Outreach, Discipleship, or Activation.
- **Impact** is Rock's impact weight, from 1 to 5.
- Rock's four types keep their names and descriptions. Each gets the order, kind, objective, impact, icon, and color above. Starting Point Class now allows one step per person, since it is a milestone.
- Foundations, Give, and Lead each get a description, an icon, a highlight color, and a call to action, like the ones Rock ships.

### Give as a step

Most published pathways treat giving as an outcome, not a step. Ministry by Design lists generosity under Contribute, though, and the sample data already has a full giving journey for every giver. So Give is a step (decision 85). It starts the week a giver becomes Consistent and ends the week they become Lapsed or Former (decision D-steps-10). A giver who comes back gets a new Give step.

## Student program: Student Path (planned)

Four steps, the same arc in a smaller form. It is for students who check in to the student service. The completion flow is Linear (Preferred), with no prerequisites.

| # | Step | Stage | Kind | Objective | Impact | Notes |
|---|---|---|---|---|---|---|
| 1 | Student Camp | Belong | Rhythm, one per summer | Outreach | 3 | A week in June or July. Often a student's first step. |
| 2 | Follow Jesus | Equip | Milestone | Discipleship | 5 | The decision to follow Jesus, often made at camp |
| 3 | Mission Trip | Contribute | Rhythm, can repeat | Activation | 4 | High school only. A week over spring break or in the summer. |
| 4 | Student Leader | Lead | Rhythm | Activation | 5 | Juniors and seniors who help lead younger students |

- **Baptism stays in one place.** A church has one Baptism step type, so a student's baptism goes on the Discipleship Path's Baptism step (decision 87). It still shows on the student's Steps tab.
- **No group step yet.** Small groups are adults only today. When student groups exist, the Student Path can add one.
- **Graduates keep their history.** A student who graduates keeps their Student Path steps.

## How adult steps are made

Every step comes from a record the data already has (decision 86). Steps and the rest of the data always agree, and every load checks that they do (see [How it is checked](#how-it-is-checked)). The loader is described in [How steps load](../loader/README.md#how-steps-load).

### Rhythm steps

- **Small Group, Serve, and Lead** follow the person's group memberships, read back from what the small groups, serving, and connection request steps wrote. The step starts the day they joined and ends the day they left, or the day the group ended while they were still in it. It links to the group as its related entity. Someone in two groups has two Small Group steps. A small group leader has a Small Group step and a Lead step for the same group. A pending request to join a small group is not a step.
- **Give** follows the giving journey. The loader runs Rock's journey stage rule every Tuesday night over the whole history window, the way Rock's Giving Automation job runs it. Each person in a giving unit gets the unit's Give steps, as each carries the unit's stage in Rock. The last run's stage is the one Rock shows, so a giver Rock shows as Consistent always has an open Give step. Givers who gave before the history window start their Give step within its first months, since that is when the data shows them consistent.
- **Status.** A rhythm step is Complete from the day it starts, and its end date stays empty while it continues. Rock's program completion counts only complete steps, so this lets a person who is in a group today count toward the path. Rock's own eRA step works differently: it stays In Progress until it ends. eRA tracks a status, but these steps record something the person did.
- **In Progress** means started but not done yet. A volunteer who waits on their team as Pending (decision 72) has an In Progress Serve step. It becomes Complete the day they become Active.

### Classes

Starting Point Class and Foundations fall on real class dates (decision D-steps-8). Each runs on a fixed rhythm that counts back from the install day, the same at every campus:

| Class | Meets | A new class starts | The current class started |
|---|---|---|---|
| Starting Point Class | Four Wednesday nights | Every four weeks | One to two weeks before install |
| Foundations | Six Tuesday nights | Every eight weeks | Two to three weeks before install |

A class is complete on its last night. So on any install day, every campus has a Starting Point class and a Foundations course In Progress. The classes meet on weeknights, so taking one does not count as coming on a Sunday.

Who takes a class, in this order:

1. **The Cast's stories.** A Cast person's household file can name a class by how many classes ago it was (`steps`). The load fails if that class cannot hold against the person's other records.
2. **Everyone the class must come before.** Every volunteer completed Starting Point before their first Serve step, which is Rock's prerequisite. It is dated in the three months before their first group or team.
3. **The current class.** Two to four people per campus, counting any Cast people in it. Starting Point takes people with an open request to serve first, since they need it before they can serve. Then it takes people who started coming or joined a group in the last year.
4. **Everyone else, up to a share.** Then active adults until the campus reaches its share: those in a group first, then others. Starting Point is dated in the three months before the person's first group or team, or otherwise in the year after the household's first visit. Foundations comes after Starting Point, in the person's first year in a group, or otherwise within two years of Starting Point.

### Baptism

- **Dates.** The Baptism step and Rock's Baptism Date person attribute always agree, because one plan writes both (decision D-steps-9). A Baptism Date in a Cast household file is kept as it is. Every other adult, Cast or generated, gets a date here.
- **Who.** 85% of volunteers and leaders, enough other active adults to reach 60% of active adults, and 20% of other adults. The Cast and the generated households each reach 60% on their own, so the Cast's baptisms are the same in every dataset.
- **Baptized Here.** About half of the planned baptisms are here, after the household's first visit. 85% of those fall on a baptism Sunday: Easter, or the third Sunday of July. The rest are any Sunday. A baptism elsewhere comes before the first visit, or before the history window when the household came before it. Rock's Baptized Here is set on the Baptism step and on the person, and the two agree. A Cast baptism is here only when it came after the household's known first visit.

### Campus and program completions

- **Campus.** Every step's campus is the person's campus, and every step is on their primary alias.
- **Program completions.** A person whose Complete steps cover all seven step types gets a program completion, the way Rock's Update Step Program Completions job records it: from their earliest Complete step's start to their latest Complete step's completion. Every step of theirs in the program, In Progress ones too, points at it. Rock's job finds nothing to add after install.

## How many

The rates are designed targets, not measured ones (decision 89). The sample church shows what healthy use of steps looks like, the same direction the notes take (decision 78). If calibration measures step use later, the measured data sets only the timing.

| Step | Designed |
|---|---|
| Starting Point Class | About 40% of active adults, and every volunteer |
| Small Group, Serve, Give, Lead | Everyone whose records show it |
| Baptism | About 60% of active adults, about half of them baptized here |
| Foundations | About 20% of active adults |
| Student Camp (planned) | About 45% of students, once for each summer they were in the student ministry |
| Follow Jesus (planned) | About 30% of students |
| Mission Trip (planned) | About 20% of high school students |
| Student Leader (planned) | About 10% of juniors and seniors |

An active adult is an adult on an active record who attended, gave, or was in a small group in the last year, the same rule prayer uses.

What each dataset loads, at the anchor 2026-10-21 and the default seed:

| Step | Core 1 and Full 7 | Core 3 |
|---|---|---|
| Starting Point Class | 51 (3 in progress) | 89 (7 in progress) |
| Small Group | 171 | 302 |
| Baptism | 64 | 126 |
| Foundations | 23 (3 in progress) | 37 (8 in progress) |
| Serve | 49 (1 in progress) | 83 (3 in progress) |
| Give | 60 | 77 |
| Lead | 20 | 48 |
| Program completions | 4 | 4 |

Core 1 and Full 7 load the same people here, because Full 7's crowd is added with `--crowd`. Core 1 has 114 active adults, 54% of them baptized (some adults on the church's edge are baptized too). In Core 3, 66% of the generated active adults are baptized. Ted Decker's completion is the one the Cast's story needs; the other three come from the same rules.

## The Cast's stories

| Person | What their steps show | Status |
|---|---|---|
| Ted Decker | The whole Discipleship Path, with a program completion. Baptized 22 years ago, in a small group, giving monthly, and leading the Production Team. | Built, checked every load |
| Brian and Wendy Gilbert | Starting Point Class In Progress. The greeters team asks for it first, which explains why their request to greet together is still In Progress. | Built, checked every load |
| Bri Hall | Starting Point Class last month, and a Serve step In Progress while she waits as a Pending greeter. The class met on weeknights, so she still has not come on a Sunday. | Built, checked every load |
| Ava Brooks | Student Camp last summer, where she made a Follow Jesus decision. | Planned |
| Benjamin Lee | Two summers of Student Camp and a mission trip last spring. | Planned |
| Jasmine Washington | The whole Student Path, with a program completion. | Planned |

Each built story is a Cast expectation (`expect.steps` in the household file), so every load checks it against the loaded data. See [Validation](../validation/README.md#cast-expectations).

## How it is checked

- **Cast expectations,** on every load and in the verify tool: the stories above.
- **Invariants** in `validation/invariants.sql`, in the verify tool: each Small Group, Serve, and Lead step matches its membership's dates, and every membership has its step; the Baptism step agrees with the Baptism Date and Baptized Here; every Serve step has a Starting Point Class completed before it; Give steps agree with the journey stage Rock shows; program completions follow Rock's job; and every step has the date keys, status, alias, and campus Rock expects.
- **Rock Cleanup** as a referee finds nothing to fix.

## Later

- **The Student Path,** when attendance per person exists.
- **eRA steps** in Rock's Core Steps program, from the attendance the data already has.
- **Links to learning and events.** When learning (LMS) and event registrations are built, Foundations can link to a learning class, and Student Camp and Mission Trip to a registration.
- **Student small groups,** which would add a group step to the Student Path.
- **Step workflows,** such as a follow-up after a baptism.
- **Person history.** Rock's step save hook adds a line to the person's History tab for each new step. The loader does not write those yet, as it does not for group memberships.
