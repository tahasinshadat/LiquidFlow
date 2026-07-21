# banks_prose.py
# Hand-authored prose pieces. PARAGRAPH_PIECES are run-on dictations whose only
# needed fix is paragraph breaks at the authored boundaries. Joining the paras
# with a single space reproduces the raw dictation exactly. MIXED_PIECES are
# prose + embedded list + prose. LONG_PIECES are 300-500 word dictations.
# native_lower pieces were spoken/transcribed without capitalization.

PARAGRAPH_PIECES = [
    {"paras": [
        "Quick update on the kitchen. The countertop guys came today and the template is done, so we're looking at install in about two weeks. They said the sink cutout adds a day, which nobody mentioned before, but fine.",
        "Meanwhile the backsplash tile is stuck in a warehouse in jersey. The store offered a similar one in stock but it's glossier than what we picked. I said I'd check with you before switching, so look at the photo I texted and tell me it's fine.",
    ]},
    {"paras": [
        "Okay so the interview went longer than planned, almost 90 minutes. The hiring manager did most of the talking for the first half, which everyone says is a good sign, and the technical questions were mostly about the migration project on my resume.",
        "The weird part was the last five minutes. They asked if I could start in three weeks instead of a month, which either means they're desperate or I'm the top choice. Recruiter said feedback by friday.",
        "Anyway I'm trying not to spiral about it. If it happens it happens. I did send the thank you note already so that box is checked.",
    ]},
    {"paras": [
        "Notes from the vet visit. Luna's weight is down to 52 pounds which is right where they want her, and the ear infection is fully cleared. The vet wants to keep her on the same food, no more table scraps, which we will pretend we're going to honor.",
        "One thing to watch, her left hip is showing early stiffness. Nothing to treat yet but she wants a recheck in six months, and we should swap the long fetch sessions for more walks.",
    ]},
    {"paras": [
        "The trip recap, because everyone keeps asking. Kyoto was the highlight, we did the bamboo grove at 7am before the crowds and it was genuinely silent, then spent the afternoon in a tea house that's been run by the same family for six generations.",
        "Osaka was chaos in the best way. We ate takoyaki from three different stands to compare, and the third one, the tiny one under the rail bridge, wins by a mile.",
        "Tokyo got cut short by the typhoon warning, so we missed the museum entirely. Honestly by day nine we were happy to sit in the hotel and watch weird game shows anyway.",
    ]},
    {"paras": [
        "Status on the migration. All read traffic has been on the new cluster since tuesday and latency is down about 30 percent at p95. Writes are still dual, and the checksum job has found zero mismatches in four days of runs.",
        "The remaining risk is the batch jobs that hit the old replica directly. There are nine of them, I've moved four, and the other five are owned by the analytics team, who say end of next week.",
        "If they slip we hold the cutover, no drama. The old cluster is paid up through the end of the month so there's no cost pressure forcing a date.",
    ]},
    {"paras": [
        "Something I noticed with the trial users this week. People who import their existing data in the first session stick around at like three times the rate of people who start from a blank workspace. The blank page is killing us.",
        "So the experiment idea is simple, make import the default first step and demote start from scratch to a text link. It's maybe two days of work and we can measure it on the same dashboard we already have.",
    ]},
    {"paras": [
        "Journal, tuesday. Slept badly because the neighbor's car alarm went off twice, and then the espresso machine chose violence and clogged mid shot. Day improved from there, mercifully.",
        "The morning writing session actually produced something. Two pages on the lighthouse chapter and I finally figured out why the middle section felt dead, it's because the narrator knows too much too early. Fixing that unlocks the whole back half.",
        "Grateful for the walk at lunch. The maples on hawthorne are turning and the whole street smells like october.",
    ]},
    {"paras": [
        "Feedback on the beta from my end after a week of daily use. The good, sync is fast, the widget is genuinely useful, and search finally finds things by content not just title.",
        "The bad, the app signs me out roughly every other day, and the export produces a file that excel opens as one giant column. Also the onboarding tooltips reappear after every update, which by update three feels like being haunted.",
        "None of this is fatal. Fix the sign out thing before launch and the rest can trail.",
    ]},
    {"paras": [
        "Plan for saturday. Farmers market first thing because the good bread sells out by nine, then the hardware store for the deck screws and the sander rental.",
        "Afternoon is the actual project. If we get the boards cut before lunch we can have the frame done by dark, and the neighbor said we can borrow his impact driver, which will save my wrist.",
        "Sunday is for nothing. I want one day where the todo list does not apply.",
    ]},
    {"paras": [
        "So the parent teacher conference. Maya's reading level is a full grade ahead, the teacher showed us the assessment and she's cruising through chapter books. Math is on grade, no concerns.",
        "The thing to work on is she rushes. First answer down, hand up, done. Teacher suggested we ask her to explain her answer out loud at homework time, apparently having to say it slows her down enough to catch her own mistakes.",
    ]},
    {"paras": [
        "Incident summary for the record. At 2:14 the checkout service started returning 500s for about 12 percent of requests. The alert fired at 2:19, and we rolled back the pricing service deploy at 2:31, full recovery by 2:36.",
        "Root cause is a null in the discount field for legacy accounts, the new code assumed it was always set. There was a migration in march that was supposed to backfill it, which silently skipped rows with archived status.",
        "Follow ups, add a null check obviously, backfill the archived rows properly, and page on the checkout error budget not just the raw rate, because 12 percent for five minutes should have paged faster than it did.",
    ]},
    {"paras": [
        "Reading notes on the negotiation book, chapter four. The core move is the calibrated question, basically asking how am I supposed to do that instead of saying no. It hands the problem back without confrontation.",
        "The part I want to remember is the bit about late night fm dj voice, slow and low when things get tense. Tried a mild version of it in the vendor call today and the price conversation stayed weirdly calm, so, one data point in favor.",
    ]},
    {"paras": [
        "Okay thinking out loud about the garage. Option one, we insulate it properly, heat it, and it becomes the gym slash workshop, that's maybe four grand all in and we lose the storage.",
        "Option two, we keep it storage and I rent the small studio on clark street for the workshop, 300 a month, which is 3,600 a year, so option one pays for itself in like 14 months.",
        "Writing that out kind of settled it. Insulation quotes this week.",
    ]},
    {"paras": [
        "The band update. We finally recorded the two songs at dave's studio on sunday, six hours, mostly because the snare kept ringing and we tried four different dampening setups before giving up and embracing it.",
        "Mixes come back friday. If they're decent the plan is to put both on bandcamp and use whistling song for the video, ben already has a storyboard involving the laundromat on 9th, do not ask.",
    ]},
    {"paras": [
        "Money check in for the month. We came in 240 under on groceries which has never happened before, credit to the meal plan whiteboard. Gas was over because of the two hospital trips but that's not a trend.",
        "The car fund hits 8,000 next month, which is the target, so after that the same 400 starts going to the vacation account. If the bonus lands in december the vacation account catches up fast and greece stops being hypothetical.",
    ]},
    {"paras": [
        "Post mortem on the dinner party, for future us. Eight people is the max for that table, nine was cozy going on hostage situation. The braise was perfect and doing it the day before meant I was an actual human during the party.",
        "The lesson we keep relearning, do not attempt a new dessert live. The tart was fine but I missed 40 minutes of my own party fussing over it. Store bought ice cream and good chocolate, done.",
    ]},
    {"native_lower": True, "paras": [
        "ok brain dump before i lose it. the pitch needs to open with the demo not the market slide, every time we lead with charts the room goes cold and we spend ten minutes clawing back attention.",
        "also thinking we cut the roadmap slide entirely and just answer roadmap questions live, feels more confident and we can't get held to dates we made up at midnight anyway.",
        "last thing, invite claire to the dry run. she asks the mean questions before the investors can and it's saved us twice.",
    ]},
    {"native_lower": True, "paras": [
        "notes to self after the long run. the new shoes are fine for 10 miles then the arch thing starts, so race day is the old pair and that decision is now final, no more relitigating it at mile 16.",
        "fueling worked, gel at 40 minutes then every 35, no stomach drama. carry one extra though because dropping one on the bridge nearly ended the whole experiment.",
        "also the playlist runs out at two hours which is a problem i have chosen to solve with more songs instead of running faster.",
    ]},
    {"native_lower": True, "paras": [
        "watched the documentary about the deep sea cameras last night and can't stop thinking about the pressure housings, they machine them from a single block of titanium because seams are where implosions start.",
        "there's a lesson in there about systems honestly, every joint is a liability, and the best design move is often just having fewer parts. writing this down before it becomes a whole thing i say in meetings.",
    ]},
    {"native_lower": True, "paras": [
        "apartment hunting notes. the pine street place has the in unit laundry and the good light but it's a fourth floor walkup and we own a couch that weighs as much as a small car.",
        "the elm place is 150 more but has the elevator and the landlord seemed like a person who returns calls. also it's two blocks from the good coffee place which is dangerous financially but excellent spiritually.",
        "leaning elm. sleeping on it and calling in the morning.",
    ]},
    {"paras": [
        "Sunday reset list is done, now thinking about the week. The big rock is the client presentation thursday, everything else bends around it. Slides are 80 percent there, the missing part is the pricing comparison and dana owes me those numbers monday.",
        "Tuesday night is blocked for the rehearsal run. Last time we winged it and the demo gods punished us accordingly, so this time we do the full run with the actual projector and the actual clicker.",
    ]},
    {"paras": [
        "The garden report, week of the 12th. The tomatoes have gone from polite to feral, the sungolds are producing faster than we can eat them and two plants have escaped their cages entirely.",
        "The zucchini situation is the usual zucchini situation. We have given away nine and the neighbors have started pretending not to be home.",
        "The one problem child is the basil, something is chewing lace patterns into the leaves. Internet says slugs, beer trap going in tonight, updates to follow.",
    ]},
    {"paras": [
        "Choir notes from tuesday. We're doing the rutter piece for the december concert and the sopranos have the high a exposed for two full bars, so ellen wants sectionals starting next week, tuesdays at 6:15 before the full rehearsal.",
        "Also concert dress changed, all black but no requirement to buy anything new, and dress rehearsal is december 19th at the church, not the school, which apparently burned people last year.",
    ]},
    {"paras": [
        "Two things from the accountant call. First, the estimated payments were slightly over all year, so there's a refund coming, around 1,900, which lands in the pain account not the fun account, we agreed on this.",
        "Second, if the freelance income stays at this rate we should look at the s corp thing seriously in january. She sent a worksheet, it's in the shared drive under taxes, and honestly it's less scary than expected.",
    ]},
    {"paras": [
        "Okay debrief from the open house. Foot traffic was strong, agent counted 34 groups, and three asked for disclosures on the spot, which she says is the real signal versus people grazing the cookies.",
        "The consistent negative was the primary bathroom, everyone clocked the dated tile. Her advice is do not renovate, just drop the list by 15k if the first week offers come in soft. Renovating now costs more than the discount and delays us into the dead season.",
    ]},
    {"paras": [
        "How the first driving lesson went, since you asked. He was nervous for about four minutes and then annoyingly competent, smooth stops, checked mirrors without being told, the instructor used the word natural which will be quoted at us for years.",
        "The homework is 30 minutes of practice in the school lot before next sunday. I have been informed that my supervising style is, quote, a lot, so dad takes the next one and I will be at home doing breathing exercises.",
    ]},
    {"paras": [
        "The book club verdict on the sea novel, since half of you skipped. The room split hard, three of us loved the slow middle section, calling it the point, and four called it 90 pages of weather.",
        "The one thing everyone agreed on is the ending earned it. Even the weather faction admitted the last chapter re reads completely differently once you know about the brother.",
        "Next month is the short story collection, under 200 pages, chosen specifically as a peace offering.",
    ]},
    {"paras": [
        "Physical therapy check in, week six. The shoulder has gone from can't sleep on it to forgetting about it most days, which the pt says is the danger zone, because this is when people quit the exercises and undo everything.",
        "So the deal is, the band work stays daily through the end of the month, then we drop to three times a week for maintenance. Overhead pressing waits until she clears it, no matter how good it feels at the gym.",
    ]},
    {"paras": [
        "Field notes from the farmers market experiment. We sold out of the sourdough by 9:40, the rye barely moved until I started cutting samples, and then it sold out too, so samples are not optional, they are the marketing department.",
        "Costs came out to about 60 for the stall and roughly 85 in ingredients and bags, revenue was 312, so real money for a saturday morning, but the 3am bake is the actual cost and we need to be honest about that before committing to weekly.",
    ]},
    {"paras": [
        "The verdict on the standing desk after one month. My lower back is noticeably happier and the 3pm slump has softened, but my feet were angry until the mat showed up, get the mat on day one, not day nine.",
        "Usage settled into a rhythm nobody predicted, standing for calls and reviews, sitting for deep work. The presets matter more than the desk brand, one touch or you will not switch, that's the whole review.",
    ]},
    {"paras": [
        "Quick note on the volunteer shift schedule. The food bank moved us from saturday morning to friday evening for the winter, 5 to 8, because the delivery truck schedule changed and saturdays are now sorting only.",
        "Carpool leaves from the church lot at 4:30. If you can't make fridays, tuesday evenings exist too, same hours, and honestly they're more short staffed on tuesdays if you're choosing.",
    ]},
    {"paras": [
        "Where we landed on the summer camp puzzle. The science camp is the 8th through the 19th, the soccer one is the 22nd through august 2nd, and the gap week in between is covered by grandma, who has already announced a museum agenda.",
        "Still unresolved is the last week of august. Options are the art camp, which she's lukewarm on, or we both burn vacation days and do the cabin, which is what everyone actually wants, deciding by friday when the art camp deposit is due.",
    ]},
    {"paras": [
        "Debrief from the demo day booth. We ran the loop demo on the tablet and it stopped exactly nobody, what stopped people was the physical prototype, cracked case and all, people picked it up and then stayed for the pitch.",
        "Collected 41 emails, four of which are actual buyers, and one guy from the hardware accelerator who said apply for the january batch and mention his name, his card is in the gray backpack front pocket, do not lose it.",
    ]},
    {"paras": [
        "The saga of the sourdough starter, day 11. It rose, it fell, it smelled like nail polish for two days, the internet said feed it more often, the internet was right, we are now twice daily and it smells like yogurt and green apples, which is apparently the goal.",
        "First real loaf attempt is saturday. Expectations are on the floor, which is where my last attempt's crumb structure also was, but this time we have a dutch oven and a grudge.",
    ]},
    {"paras": [
        "Notes from the accessibility review of the signup flow. The contrast issues are all in the muted gray text, one token fix cascades everywhere, easy win. Focus order on the payment step jumps from card number to the promo link and skips the expiry field, that's a real bug.",
        "The screen reader run found the worst one, the error messages appear visually but are never announced, so a blind user just hits submit into silence. That one goes to the top of the list.",
    ]},
    {"paras": [
        "Sunday planning, out loud. The freezer inventory says we're rich in soup and bankrupt in actual dinners, so the cook this week is two sheet pan meals and the big curry, that covers monday through thursday with lunches falling out the bottom.",
        "Friday we're out for rosa's birthday, and saturday is the neighborhood chili thing, so no cooking friday or saturday, which the schedule gods have never granted us before.",
    ]},
    {"native_lower": True, "paras": [
        "thinking about why the tuesday post did numbers and the thursday one died. tuesday was a specific story with a number in the title, thursday was advice with a vague title, this is the third time the pattern holds.",
        "so the rule going forward, every title needs either a number or a name in it, and if a draft is advice shaped it gets rewritten as the story of the time the advice was learned. calendar updated, we'll see if the theory survives contact with reality.",
    ]},
    {"native_lower": True, "paras": [
        "voice memo about the chess opening thing. i keep getting wrecked in the same line of the caro kann, the one where they push the pawn and my bishop ends up buried, the engine says the fix is trading it off early even though it feels wrong.",
        "plan is 20 games of just that line against the bot this week, feelings disabled, and we review sunday whether the engine or my feelings were right. spoiler, it will be the engine.",
    ]},
    {"paras": [
        "Wrap up from the science fair. The volcano kids won the crowd but the judges gave first to the girl who tested which grocery bags decompose fastest, buried them in september and photographed them monthly, actual longitudinal data from a seventh grader.",
        "Our guy took third with the bridge project and is already talking about next year, something involving drones and I have chosen not to ask follow up questions until summer.",
    ]},
    {"paras": [
        "The neighborhood watch update nobody asked for. The package thefts stopped after the hendersons put up the camera, either coincidence or the sign did the work, nobody knows, the camera has thus far recorded exclusively raccoons.",
        "Next meeting is at the library on the 9th, main agenda is the crosswalk petition, we need 50 signatures and we're at 33, so bring a neighbor or forge a personality that convinces one.",
    ]},
    {"paras": [
        "Where the novel is at, for accountability purposes. Draft two of part one is done, 31,000 words, and the pacing problem in the ferry chapters is fixed by cutting the flashback entirely, which hurt for a day and now feels obvious.",
        "Part two is a swamp. The plan is to outline it beat by beat this week instead of wandering in with a machete again, and the writing group gets chapters 9 and 10 on friday whether I like them or not.",
    ]},
    {"paras": [
        "Takeaways from the first aid course, writing them while fresh. Compressions are way more physical than tv suggests, the instructor had us go two full minutes and my arms were done, the lesson being trade off every two minutes, no heroes.",
        "The other thing that stuck, you say call 911 to a specific person, you point at them, because a crowd asked generally does nothing. Recert is in two years, card is in the email.",
    ]},
    {"paras": [
        "Notes on switching the kids to the later school bus. Pickup moves from 7:10 to 7:52, which means actual breakfast instead of the granola bar sprint, and the stop moves to the corner by the blue house, two minutes closer.",
        "The catch is it arrives at school only eight minutes before the bell, so any delay is a tardy. We're trying it for two weeks starting monday and keeping the early bus as the fallback if the timing proves cursed.",
    ]},
    {"paras": [
        "The espresso dial in log, new beans edition. Started at 18 grams in, 36 out, 28 seconds, and it tasted like biting a lemon that owed me money. Coarser and hotter helped, 94 degrees, 40 out, now it's balanced with the chocolate thing the bag promised.",
        "Noting for future me, this roaster's beans need a week of rest minimum, the first day shots were undrinkable and it was not the grinder's fault, apologies were issued to the grinder.",
    ]},
    {"paras": [
        "Recap of the insurance phone maze, so nobody repeats it. The claim for the windshield goes through the glass line, not the main line, direct number is on the card in the glovebox, and saying agent at the robot three times does eventually summon a human.",
        "The human, once summoned, was great, approved the mobile replacement for thursday at the office parking lot, no deductible because of the full glass rider we forgot we had. The rider stays on the policy forever, decision made.",
    ]},
]

LONG_PIECES = [
    {"paras": [
        "Full write up of the launch week, because we'll want this later. Monday we flipped the flag for the waitlist cohort, about 1,200 accounts, and the first hour was quiet in the way that's either good or catastrophic. It was good, activation on day one came in at 41 percent against a guess of 30, and the support queue stayed under ten tickets, most of them password stuff, nothing structural.",
        "Tuesday the newsletter went out and that's when the graph did the thing. Traffic peaked at 9x baseline around 11am, the api held, and the only wobble was image resizing backing up for about 20 minutes, which showed up as slow avatars and nothing else. Wednesday a mid sized creator picked it up on their own, no outreach, and that single video drove more signups than the newsletter, which says something about where the actual audience lives.",
        "Thursday was the reckoning with reality. Churn from the tourist cohort hit as expected, day three retention settled at 22 percent, which is fine for tourists but the number to move is the 41 percent activation converting to week one habit. The pattern in the data is blunt, people who create a second project on their first day retain at triple the rate of people who don't, so the entire onboarding conversation is now about engineering the second project.",
        "Friday we shipped the two smallest fixes from the feedback pile, renamed the confusing button, and called the week. Overall read, the product is real, the funnel leaks where we thought it would, and the next four weeks are about the second project moment and nothing else. Team is tired and proud, correct combination.",
        "For the record, the stack held up better than the team's sleep schedule did. Peak concurrency hit just under 4,000, the database never went above 60 percent, and the one 2am page was a false alarm from the synthetic monitor tripping over its own timeout. We owe the load testing week an apology for calling it overkill, it bought us this entire boring, wonderful launch.",
    ]},
    {"paras": [
        "The whole saga of the basement water thing, documented for the insurance file and for our own sanity. We first noticed the smell on the 3rd, faint, only near the storage corner, and wrote it off as old cardboard. On the 6th the corner carpet was damp to the touch, and the dehumidifier we threw at it was a bandaid on a problem we hadn't found yet.",
        "The 8th was the rainstorm, and that made diagnosis easy, water was visibly wicking in along the seam where the foundation meets the slab on the northeast corner, the corner where the downspout dumps. The downspout extension had cracked at the elbow, so the roof was delivering its entire northeast quarter directly against the foundation, probably for months.",
        "Fixes so far, new downspout extension carrying water eight feet out, done same day for 30 dollars, and the grading guy came on the 12th and confirmed the soil there had settled into a slope toward the house, his crew regrades it thursday for 900. Inside, the wet section of carpet is cut out and gone, fans ran for three days, moisture meter readings are back to matching the dry side of the basement.",
        "The open question is the hairline crack in the foundation seam, which two contractors have now looked at. One says injection seal it now for 1,200, the other says with the water source fixed it'll stay dry and to save the money and watch it for a season. We're going with watch it, marked with dated tape, photos monthly, and if it weeps at all during spring melt we do the injection with no further debate.",
        "Costs so far for the file, 30 for the downspout parts, 900 for the regrading, 180 for the dehumidifier we'd have bought eventually anyway, and zero on the carpet because the remnant guy takes scraps. Insurance says none of it meets the deductible, which is annoying but also means no claim on the record, and the agent confirmed the coverage would kick in if the crack ever actually fails. Filing every receipt and photo in the house folder either way.",
    ]},
    {"paras": [
        "Everything from the college visit weekend while it's fresh. The friday tour at the state school was a downpour, which was honestly a useful stress test, campus still felt alive, the tour guide answered the annoying money questions with actual numbers, and the engineering building had that 2am energy with whiteboards full of half erased math even on a friday.",
        "The information session covered the honors college and this is where it got interesting, priority registration, smaller sections for the weed out courses, and a research placement freshman year, and the gpa cutoff to stay in is a 3.4, which is demanding but not cruel. Housing for honors is the renovated dorm, we saw a room, it has the good kind of window.",
        "Saturday was the private school two hours north, and the contrast was immediate, a third the size, professors teach everything including intro courses, and the aid letter math they walked us through would put the real cost within 4k a year of the state option, which is not the gap we assumed. The vibe is quieter, more hiking club than football, and he lit up in the robotics lab in a way he didn't all friday.",
        "His gut read in the car, state school won friday, small school won saturday, and the tiebreaker is going to be the admitted students overnight in the spring. Deadlines to hold, state app due november 1 for the scholarship pool, small school is rolling but aid gets thin after january, so both apps go in by halloween and we keep both doors open all winter.",
        "Small logistics footnote while I remember it, the state school waives the application fee if you apply during their october open house window, and the small school does the same if you interview, which he should do anyway because he interviews better than his essays suggest. Both campuses are within four hours of home, both have direct bus routes, and the cost gap after aid is small enough that we agreed the decision belongs to him, not the spreadsheet.",
    ]},
    {"paras": [
        "Long overdue braindump on the home network rebuild before I forget what past me was thinking. The old setup was the isp combo box doing everything badly, wifi dying in the back bedrooms, and the smart home stuff randomly falling off the network every few days, which is how you end up with a light switch that requires a reboot.",
        "New shape, the isp box is now modem only, routing moved to the little fanless box running opnsense, and access points went up in the hallway ceiling and the garage, wired back to the switch in the closet. The wifi is one network name, roaming actually works, and the back bedroom went from one bar of sadness to full speed, measured 480 down at the desk that used to get 40.",
        "The smart home stuff got quarantined onto its own vlan with no route to the real network, because the cheap plugs phone home to wherever and I'd rather they gossip in a locked room. Printer lives there too. The only casualty was the old chromecast which refused to cross vlans for casting until the mdns repeater was set up, one evening lost to that, worth it.",
        "Remaining list, label the patch panel because future me deserves dignity, put the config backup somewhere that isn't the machine it configures, and mount the closet shelf properly instead of the current arrangement, which is a router balanced on a shoebox that once held the shoes I'm wearing. Total spend came in around 420 all told, and the household has not had to hear the phrase have you tried unplugging it in three weeks.",
        "Performance numbers for posterity, because future me will want receipts. Wired desktop stayed at 940, no change, as expected. The office laptop went from 210 to 610, the kitchen tablet from 80 to 400, and the garage, which previously had no usable signal at all, now pulls 300 and change, meaning the shop speaker finally streams without the nightly dropout ritual. Latency under load is the real win, video calls no longer die when someone starts a backup upstairs.",
    ]},
    {"paras": [
        "Race report from the half, while my legs still remember it. Weather was 44 degrees and overcast, which is free speed, and the corral start was clean, crossed the line 90 seconds after the gun with room to run immediately. The plan was 9:20s through mile 10 and then whatever was left, and for once the plan and the body agreed.",
        "Miles one through six were metronome stuff, 9:18, 9:22, 9:19, ticking off exactly like the training block said they would. The bridge at mile seven is the course's one real insult, half a mile of steady up into the wind, and the pace dipped to 9:50 but the effort stayed even, which the coach voice in my head kept insisting is the correct trade.",
        "The gel at 40 minutes went down fine, the one at 80 minutes fought back a little, note for next time, take it before the water station not after. Miles 10 and 11 are where past races have unraveled, and this time they were 9:15 and 9:12, passing people, which is a sensation I could get addicted to.",
        "The finish came in at 2:01:48, four minutes off the old personal best, and close enough to two hours to make the decision for me, spring race, same course, sub two or bust. Recovery notes, the calves are furious, the knee that worried me is silent, and the post race burger was, without exaggeration, the greatest meal ever assembled by human hands.",
        "Gear notes before they fade, the throwaway layer at the start was perfect and I never missed it, the gloves went in the pocket at mile three exactly as planned, and the anti chafe stick earned its permanent roster spot in ways I will not elaborate on. Watch battery finished at 61 percent, so navigation mode will survive a full marathon if that madness ever becomes official. Next up is two weeks of easy running and absolutely no signing up for anything while the endorphins are still doing the talking.",
    ]},
    {"paras": [
        "Minutes from the community garden annual meeting, the unofficial version. Attendance was 19 of 31 plot holders, quorum by two people, and the treasurer opened with the news that the water bill nearly doubled this year, which set the tone for the whole budget conversation. We have 1,140 in the account, the bill was 780, and the math from there wrote its own agenda.",
        "The main vote, plot fees go from 40 to 55 a season, passed 14 to 5 after the treasurer showed the three year trend. The counterproposal to meter each row and charge by usage was tabled, mostly because nobody volunteered to be the person reading meters and sending awkward invoices to their neighbors.",
        "Second item, the compost situation. The pile has become, quoting the minutes directly, a municipal concern, and the fix is a proper three bay system that marcus has offered to build if the garden covers materials, estimated at 160. Approved by voice vote with genuine enthusiasm, construction the first weekend of march, and a signup sheet exists for people to be taught to turn it correctly.",
        "Last item and the only real controversy, the waitlist policy. Current members can hold a second plot for one more season, but starting next year second plots release to the waitlist, which now has 22 names on it. The vote was closer, 10 to 9, and feelings were had, but the argument that a garden with a three year waitlist is failing its actual purpose carried the room. Meeting closed with the seed swap date set for february 22nd, potluck rules, no zucchini jokes were survived.",
        "Housekeeping items that didn't need votes, the tool shed lock code changes on the 1st and goes out by email, the wheelbarrow with the bent handle is retired to the compost area as a staging cart, and the lost and found bucket by the spigot gets emptied into the donation bin monthly. The bulletin board is getting a laminated map of plot assignments so new members stop watering the wrong tomatoes, which happened twice last season and caused one extremely polite standoff.",
    ]},
    {"paras": [
        "Notes from three days of jury duty, written mostly so I stop retelling it at parties. Day one was four hours of waiting room purgatory with a orientation video from approximately 1998, and then suddenly it was real, sixty of us filed into a courtroom for selection on a civil case about a delivery truck and a parking structure gate.",
        "Voir dire was genuinely fascinating, the lawyers weren't looking for smart or dumb, they were looking for priors, anyone who'd ever fought an insurance company got a follow up question, anyone who said the phrase corporations always got struck by one side, anyone who said people sue too much got struck by the other. I said I could follow evidence, which is apparently the password, and got seated as juror eight.",
        "The case itself took a day and a half, two witnesses, a surprising amount of testimony about gate maintenance logs, and a structural engineer whose entire vibe suggested he had waited his whole life to explain torsion to a captive audience. Deliberation took three hours, of which maybe forty minutes was the actual dispute, the rest was one juror needing to re examine every photo and honestly, respect.",
        "We found for the plaintiff on the gate but knocked the damages way down from the ask, which felt right to everyone by the end. Takeaways, bring a book, the chairs are a war crime, and the system up close is slower and more careful and more human than I expected, would grudgingly serve again.",
        "Practical notes for whoever gets summoned next, parking is validated only at the garage on court street, not the one attached to the building, a distinction that cost me twelve dollars to learn. Lunch options within walking distance are one good sandwich place and a food truck lottery, and the courtroom runs cold enough that the bailiff owns a visible sweater collection. Phones go in lockers on trial days, so the book is not optional, it is survival equipment.",
    ]},
    {"paras": [
        "The great phone plan audit, conducted at the kitchen table with spreadsheets and grievances. Current state, we pay 168 a month for four lines on the big carrier, of which one line belongs to a tablet nobody has charged since easter, and the plan includes streaming perks we already pay for separately like fools.",
        "The audit found the following, dad uses 3 gigs a month, mom uses 2, the kids use the entire electromagnetic spectrum, and the tablet line costs 20 a month to keep a dead device theoretically connected. Killing the tablet line and dropping the perk bundle takes us to 128 with zero behavior change, that's the do nothing option and it's already 480 a year back.",
        "The bigger move is the mvno math. The budget carrier runs on the same towers, and their family plan with two unlimited lines for the kids and two 5 gig lines for the adults comes to 90 a month. The catch list is real but short, no international roaming included, which matters twice a year, and customer service is a chat window instead of a store, which matters approximately never.",
        "Decision, we port the adults first as the test group, kids follow next month if nothing catches fire, and the tablet line dies today with full honors. Projected steady state is 90 a month against the old 168, which is 936 a year, or as it was ratified at the table, greece money. The tablet has been informed. It took the news as well as could be expected.",
        "Implementation notes so this transfers smoothly, both adult numbers port with the account number and the transfer pin, which lives in the password manager under the carrier's name, and the port has to start on a weekday morning unless we enjoy 48 hours of limbo. The kids' phones are unlocked already, checked this weekend, and the new sims arrive tuesday. Autopay dies on the old account only after the final bill posts, because that mistake is apparently a rite of passage on every forum I read.",
    ]},
]

# Mixed prose + list pieces. Rendered: pre prose, optional label line, items
# (bulleted or numbered), optional post prose. Spoken form linearizes items
# with connectives.
MIXED_PIECES = [
    {"pre": "quick update before I sign off, the deploy went out clean and the dashboards look normal.", "label": "three things came up in review though", "items": ["the retry logic needs a cap", "the log lines are missing request ids", "staging still points at the old bucket"], "post": "none of them block the release but let's clear them monday.", "ordered": False},
    {"pre": "okay dinner plan for the week is settled.", "label": "the cook nights are", "items": ["monday sheet pan chicken", "tuesday the big curry", "thursday breakfast for dinner"], "post": "wednesday we're at the school thing and friday is leftovers, do not let me order pizza.", "ordered": False},
    {"pre": "talked to the landscaper and we're going ahead with the spring cleanup.", "label": "the quote covers", "items": ["pruning the front hedges", "mulching all the beds", "reseeding the dead strip by the driveway", "hauling away the brush pile"], "post": "he needs the gate unlocked thursday morning and payment is due on completion.", "ordered": False},
    {"pre": "the study plan for the boards, final answer.", "label": None, "items": ["two practice blocks every weekday morning", "full length exam every saturday", "sunday is review of the week's wrong answers only"], "post": "no new material after the 15th, just consolidation.", "ordered": True},
    {"pre": "before we hand the apartment back we have to close out the checklist.", "label": "still open", "items": ["patch the nail holes in the bedroom", "steam the living room carpet", "return both fobs to the office", "photograph every room after it's empty"], "post": "the walkthrough is friday at 2 and the deposit depends on it.", "ordered": False},
    {"pre": "so the conference schedule dropped and I made my picks.", "label": "locked in", "items": ["the keynote obviously", "the workshop on embedded ml at 11", "the panel with the two robotics founders", "the hallway track, which is where the real conference happens"], "post": "everything else is optional depending on coffee proximity.", "ordered": False},
    {"pre": "here's where we landed after the budget meeting with the wedding planner.", "label": "the cuts are", "items": ["the ice sculpture, obviously", "upgraded chairs nobody would have noticed", "the second photographer for the reception"], "post": "that frees up about 3,800 which goes straight to the band, priorities intact.", "ordered": False},
    {"pre": "the onboarding revamp kicked off today and we split the work.", "label": None, "items": ["priya owns the welcome email sequence", "marcus takes the empty states", "I've got the checklist widget", "design reviews everything as one flow on friday"], "post": "target is behind a flag by the end of the sprint.", "ordered": False},
    {"pre": "car got the full inspection before the road trip.", "label": "they flagged", "items": ["front pads at 30 percent", "a wiper blade that's more decorative than functional", "the cabin filter, which apparently contained a leaf collection"], "post": "did the wiper and filter today, pads can wait until after the trip per the mechanic.", "ordered": False},
    {"pre": "team decided on the offsite agenda after way too much debate.", "label": "day one is", "items": ["the roadmap session in the morning", "the customer call recordings over lunch", "the postmortem workshop until 3", "hiking, which is mandatory fun but is actually fun"], "post": "day two is unstructured pairing time and we protect it with our lives.", "ordered": False},
    {"pre": "the pediatrician visit was quick and all good news.", "label": "for the file", "items": ["height 42 inches, 60th percentile", "weight right on the curve", "vision screen passed", "one shot, handled with minimal drama and one sticker"], "post": "next checkup is in a year unless the ear thing comes back.", "ordered": False},
    {"pre": "reorganized the workshop and the new rule is everything has a zone.", "label": "the zones are", "items": ["wood and cutting along the back wall", "electronics bench under the window", "finishing table by the vent", "hardware wall on the pegboard, sorted by size"], "post": "anything found outside its zone gets confiscated by the shop gremlin, which is me.", "ordered": False},
    {"pre": "signed us up for the cooking class series, it's four sessions.", "label": None, "items": ["knife skills on the 3rd", "sauces on the 10th", "pasta from scratch on the 17th", "a full menu we cook and then eat on the 24th"], "post": "it's on both calendars, no meetings on those thursdays.", "ordered": True},
    {"pre": "the freelance pipeline check, because I promised myself monthly honesty.", "label": "active right now", "items": ["the bakery site, half paid, launches next week", "the law firm rebrand, waiting on their logo feedback", "the newsletter template gig, small but repeat business"], "post": "pipeline is thinner after that, so two cold pitches go out this week.", "ordered": False},
    {"pre": "got the reading list from the professor for the seminar.", "label": "in order", "items": ["the tufte chapters as a warmup", "the norman book in full", "three papers on the moodle page", "one case study we each present"], "post": "presentations are assigned alphabetically so I'm up second week, of course.", "ordered": True},
    {"pre": "the move is officially booked for the 28th.", "label": "before then we need to", "items": ["reserve the freight elevator at the new place", "transfer the utilities to start on the 27th", "eat the entire freezer", "find the screws for the bed frame, which entered witness protection last move"], "post": "the movers said eight boxes of books maximum and we are going to lie to them.", "ordered": False},
    {"pre": "the marathon training block starts monday, 16 weeks out.", "label": "the weekly shape is", "items": ["tuesday intervals at the track", "thursday tempo before work", "saturday long run, building to 20", "everything else easy or off, no exceptions"], "post": "the plan is taped to the fridge, the fridge is now my coach.", "ordered": False},
    {"pre": "we scoped the bathroom refresh and agreed to keep it cosmetic.", "label": "doing", "items": ["new vanity and faucet", "regrout the shower", "paint everything that holds still", "swap the boob light for something from this century"], "post": "not touching the tile layout or the plumbing, that way lies a second mortgage.", "ordered": False},
    {"pre": "quarterly goals got finalized in the review this morning.", "label": None, "items": ["ship the mobile redesign to 100 percent", "get support response time under four hours", "hire the two backend roles", "kill the legacy exporter for good"], "post": "everything else is upside, these four are the commitments.", "ordered": True},
    {"pre": "the potluck signup shook out fine, nobody's doubling up.", "label": "confirmed so far", "items": ["rosa's tamales", "the chen family noodle salad", "dev's brisket, which sold out in minutes last year", "our lemon bars"], "post": "still nobody on drinks, so we might grab a case of sparkling water as backup.", "ordered": False},
    {"pre": "the garage sale prep is down to the wire, saturday 8am sharp.", "label": "left to do", "items": ["price the furniture with the green stickers", "make the corner signs tonight", "get 60 bucks of small bills from the bank", "stage the free box by the curb early, it draws people in"], "post": "everything unsold by 1pm goes straight in the donation van, no renegotiations.", "ordered": False},
    {"pre": "the api deprecation plan is approved, we're doing the slow kind.", "label": None, "items": ["announce with a six month runway", "add deprecation headers next release", "email the top 40 consumers directly", "brownouts for an hour a week starting month five", "shut it off on the published date, actually"], "post": "the last one is the hard part and the reason the plan is written down.", "ordered": True},
    {"pre": "orientation packet finally came for the exchange semester.", "label": "the immediate paperwork is", "items": ["the visa appointment, book it this week", "proof of insurance in their format", "the housing preference form, due friday", "transcripts, sealed, which means a registrar visit"], "post": "flights can wait until the housing assignment lands next month.", "ordered": False},
    {"pre": "we did the seed order for spring, going slightly overboard as tradition demands.", "label": "new experiments this year", "items": ["the striped roma tomatoes", "ground cherries, which the internet promises are magic", "a hot pepper called lemon spice", "actual brussels sprouts, attempt number three"], "post": "the reliable stuff is reordered same as always, the experiments get the back bed.", "ordered": False},
    {"pre": "did the annual password cleanup, which was overdue in ways I won't confess.", "label": "the damage", "items": ["four accounts still shared the old standby password", "two dead email addresses were still recovery contacts", "the bank login had no second factor, somehow"], "post": "everything's in the manager now with generated passwords, and the recovery contacts point at the right inbox.", "ordered": False},
]
