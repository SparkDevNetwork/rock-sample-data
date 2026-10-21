# Steps

The plan for steps in the sample data, and why. Nothing here is built yet. The decisions are D-steps-1 to D-steps-7 in [Decisions](decisions.md). When the build starts, the numbers move to `configuration/steps.json`, so they can be tuned in one place.

## The idea

The sample church has two step programs. The adult program shows the five to seven things the church hopes every adult will do. The student program is a shorter version of the same path for Jr High and High School.

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

The plan follows the serving pattern (decision 68): keep Rock's program, its step types, and their Guids, and build them out. Any Rock feature that looks them up by Guid still works.

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
| 7 | Lead (new) | Lead | Rhythm | Activation | 5 | Each time the person leads a group or a team |

- **Kind** is Rock's engagement type. A milestone happens once. A rhythm is a habit that starts and can stop.
- **Objective** is Rock's organizational objective: Outreach, Discipleship, or Activation.
- **Impact** is Rock's impact weight, from 1 to 5.
- Each new step type gets a description, an icon, a highlight color, and a call to action, like the ones Rock ships.

### Give as a step

Most published pathways treat giving as an outcome, not a step. Ministry by Design lists generosity under Contribute, though, and the sample data already has a full giving journey for every giver. So Give is a step (decision D-steps-3). It starts the day a giver becomes Consistent and ends the day they become Lapsed. A giver who comes back gets a new Give step.

## Student program: Student Path

Four steps, the same arc in a smaller form. It is for students who check in to the student service. The completion flow is Linear (Preferred), with no prerequisites.

| # | Step | Stage | Kind | Objective | Impact | Notes |
|---|---|---|---|---|---|---|
| 1 | Student Camp | Belong | Rhythm, one per summer | Outreach | 3 | A week in June or July. Often a student's first step. |
| 2 | Follow Jesus | Equip | Milestone | Discipleship | 5 | The decision to follow Jesus, often made at camp |
| 3 | Mission Trip | Contribute | Rhythm, can repeat | Activation | 4 | High school only. A week over spring break or in the summer. |
| 4 | Student Leader | Lead | Rhythm | Activation | 5 | Juniors and seniors who help lead younger students |

- **Baptism stays in one place.** A church has one Baptism step type, so a student's baptism goes on the Discipleship Path's Baptism step (decision D-steps-5). It still shows on the student's Steps tab.
- **No group step yet.** Small groups are adults only today. When student groups exist, the Student Path can add one.
- **Graduates keep their history.** A student who graduates keeps their Student Path steps.

## How steps are made

Every step comes from a record the data already has (decision D-steps-4). Steps and the rest of the data always agree.

### Rhythm steps

- **Small Group, Serve, and Lead** follow the person's group memberships. The step starts the day they joined, ends the day they left, and links to the group as its related entity. Someone in two groups has two Small Group steps.
- **Give** follows the giving journey (see above).
- **Status.** A rhythm step is Complete from the day it starts, and its end date stays empty while it continues. Rock's program completion counts only complete steps, so this lets a person who is in a group today count toward the path. Rock's own eRA step works differently: it stays In Progress until it ends. eRA tracks a status, but these steps record something the person did.
- **In Progress** means started but not done yet. A volunteer who waits on their team as Pending (decision 72) has an In Progress Serve step. It becomes Complete the day they become Active.

### Milestone steps

- **Starting Point Class.** Dated in the three months before the person's first group or team. Every volunteer has one, since Serve requires it. Each campus also runs a class every month, so a few people are In Progress at any time.
- **Baptism.** Dated from the person's Baptism Date attribute, and the two always agree. Rock's "Baptized Here" is Yes when the baptism came after the household's first visit. Baptisms cluster on baptism Sundays, such as Easter and a summer Sunday.
- **Foundations.** Dated after Starting Point, often in the first year in a group. A current group of students at each campus is In Progress.
- **Campus.** Every step's campus is the person's campus.

### How many

These are designed targets, not measured rates (decision D-steps-7). The sample church shows what healthy use of steps looks like, the same direction the notes take (decision 78). If calibration measures step use later, the measured data sets only the timing.

| Step | Who has one |
|---|---|
| Starting Point Class | About 40% of active adults, and every volunteer |
| Small Group, Serve, Give, Lead | Everyone whose records show it |
| Baptism | About 60% of active adults, about half of them baptized here |
| Foundations | About 20% of active adults |
| Student Camp | About 45% of students, once for each summer they were in the student ministry |
| Follow Jesus | About 30% of students |
| Mission Trip | About 20% of high school students |
| Student Leader | About 10% of juniors and seniors |

A person who completes every step gets a program completion, the way Rock's Update Step Program Completions job records it. Only a few percent of adults finish the whole Discipleship Path, and that is realistic.

## The Cast's stories

| Person | What their steps show |
|---|---|
| Ted Decker | The whole Discipleship Path, with a program completion. Baptized 22 years ago, in a small group, giving monthly, and leading the Production Team. |
| Brian and Wendy Gilbert | Starting Point Class In Progress. It explains why their request to greet together is still In Progress. |
| Bri Hall | Starting Point Class last month, and a Serve step In Progress while she waits as a Pending greeter. |
| Ava Brooks | Student Camp last summer, where she made a Follow Jesus decision. |
| Benjamin Lee | Two summers of Student Camp and a mission trip last spring. |
| Jasmine Washington | The whole Student Path, with a program completion. |

The Cast validator will check each story against the person's other records when the steps are built.

## Later

- **eRA steps** in Rock's Core Steps program, from the attendance the data already has.
- **Links to learning and events.** When learning (LMS) and event registrations are built, Foundations can link to a learning class, and Student Camp and Mission Trip to a registration.
- **Student small groups,** which would add a group step to the Student Path.
- **Step workflows,** such as a follow-up after a baptism.
