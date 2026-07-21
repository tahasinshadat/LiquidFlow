# banks_msgs.py
# Emails (dictated), meeting-note dumps with embedded lists, "new line"
# command pieces, and "bullet point" command pieces. The generator renders the
# raw dictation and the formatted output from the same structured source.

# Emails: greeting / body paragraphs / closing / signature.
# Raw dictation is everything joined with spaces (optionally with spoken
# "new paragraph" commands inserted); formatted output puts greeting on its own
# line, blank lines between paragraphs, and closing over signature.
EMAILS = [
    {"greeting": "hi sarah", "paras": [
        "hope your week is going okay, I wanted to follow up on the invoice from last month, number 4471, it still shows as unpaid on our end",
        "if it's already in process feel free to ignore this, but if anything is missing on the paperwork side just tell me what you need and I'll send it over today"],
     "closing": "thanks so much", "sig": "taha"},
    {"greeting": "hey mr alvarez", "paras": [
        "this is jordan from unit 12, the bathroom sink has been draining slowly for about a week and as of this morning it's basically not draining at all",
        "I tried the usual stuff, nothing helped, so I think it needs a professional look, I'm home after 4 most days if someone wants to come by"],
     "closing": "thank you", "sig": "jordan"},
    {"greeting": "hi professor lin", "paras": [
        "I'm in your tuesday section of stats 210, I wanted to ask about the problem set due friday, question 6 references a dataset that doesn't seem to be on the course page",
        "if it's posted somewhere else could you point me to it, and if not, could we get a short extension since a few of us are stuck on the same thing"],
     "closing": "best", "sig": "maya chen"},
    {"greeting": "hi team", "paras": [
        "quick note before the sprint starts monday, the staging environment will be down saturday morning for the database upgrade, roughly 8 to 11",
        "if you have demos or testing planned, do them friday or wait until saturday afternoon, and ping me if the timing is a real problem for anyone"],
     "closing": "thanks", "sig": "priya"},
    {"greeting": "hello", "paras": [
        "I placed order 88214 last tuesday and the tracking hasn't updated since it printed a label a week ago",
        "could you check whether it actually shipped, and if it's lost I'd rather get a replacement sent than wait for the investigation to finish"],
     "closing": "thanks for your help", "sig": "dana whitfield"},
    {"greeting": "hi coach", "paras": [
        "ben will miss practice thursday, we have a family thing out of town, he'll be back for the saturday game and he's been doing the drills at home",
        "also his cleats finally died so he'll be in new ones this weekend, breaking them in as we speak"],
     "closing": "see you saturday", "sig": "marcus"},
    {"greeting": "hi elena", "paras": [
        "great talking earlier, as promised here's the summary of what we'd cover in the first engagement, an audit of the current pipeline, a written report with the quick wins ranked, and a working session with your team to hand everything off",
        "timeline is about three weeks from kickoff and the quote I mentioned holds through the end of the month"],
     "closing": "talk soon", "sig": "sam"},
    {"greeting": "hey neighbors", "paras": [
        "heads up we're having some work done on the driveway starting wednesday, there will be a small crew and a concrete truck in the morning, should be wrapped by friday",
        "street parking on our side will be tight those mornings, sorry in advance, the finished driveway promises to be extremely boring and gray"],
     "closing": "thanks for the patience", "sig": "the patels in 44"},
    {"greeting": "hi dr osei's office", "paras": [
        "I need to reschedule my appointment on the 22nd, something came up at work that I can't move",
        "any weekday morning the following week works for me, the earlier the better, you can reach me at this email or the number on file"],
     "closing": "thank you", "sig": "chris"},
    {"greeting": "hi rachel", "paras": [
        "following up from the career fair, I'm the one who asked about the embedded team, you mentioned sending over the internship posting when it goes live",
        "I've attached my resume so it's handy, and I'm happy to do a call whenever is convenient, my schedule is flexible around classes"],
     "closing": "best regards", "sig": "omar hassan"},
    {"greeting": "hello support", "paras": [
        "since the latest update the app crashes on launch on my pixel 8, I've already tried reinstalling and clearing the cache, no luck",
        "happy to send logs if you tell me how to export them, the crash happens before I can reach any settings screen"],
     "closing": "thanks", "sig": "k. yamamoto"},
    {"greeting": "hi aunt rosa", "paras": [
        "mom asked me to send you the final headcount for sunday, we're at 14 adults and 6 kids, two of the adults are vegetarian",
        "we're bringing the folding table and the big coffee maker, and dad says he's bringing his famous beans whether anyone asks him to or not"],
     "closing": "love you, see you sunday", "sig": "sofia"},
    {"greeting": "hi mr thompson", "paras": [
        "emma will be out of school tuesday and wednesday next week for a family wedding out of state",
        "we'd love to grab any assignments ahead of time so she doesn't fall behind, she's especially worried about missing the fractions review"],
     "closing": "thank you", "sig": "grace liu"},
    {"greeting": "hey mike", "paras": [
        "the venue got back to us, they can do the 18th but not the 25th, and the deposit went up 200 since last year because everything is like that now",
        "if the 18th works for the rest of the group I say we lock it today before someone else takes it, can you poll the chat"],
     "closing": "cheers", "sig": "alex"},
    {"greeting": "hi it team", "paras": [
        "my laptop has been blue screening about once a day since the security update last week, usually when docking or undocking",
        "I've saved the error codes from the last three crashes and can bring the machine by whenever, mornings are best, I sit on the third floor by the plants"],
     "closing": "thanks in advance", "sig": "nina"},
    {"greeting": "hello", "paras": [
        "we stayed in the lakeside cabin the weekend of the 9th and left a green fleece jacket in the bedroom closet, kid sized",
        "if the cleaning crew found it we're happy to pay shipping, or we can grab it next month since we'll be up that way again"],
     "closing": "much appreciated", "sig": "the okafors"},
    {"greeting": "hi jess", "paras": [
        "the client signed, official kickoff is monday, so let's grab 30 minutes friday to split the workstream before they start assigning it for us",
        "I think the natural split is you take the data model and I take the integrations, but I'm flexible if you'd rather swap"],
     "closing": "talk friday", "sig": "derek"},
    {"greeting": "dear hiring team", "paras": [
        "I interviewed for the operations coordinator role on the 14th and wanted to thank the panel for the conversation, the walkthrough of the warehouse workflow was genuinely interesting",
        "the role sounds like a strong fit for the scheduling and vendor work I've been doing the last three years, and I'd be glad to provide references or anything else you need"],
     "closing": "sincerely", "sig": "tyler brooks"},
    {"greeting": "hi hoa board", "paras": [
        "submitting the request form for a fence repair at 118 birchwood, same height and color as existing, the winter storm leaned two posts and they need replacing",
        "the contractor is licensed and can share insurance certificates, work would take one day and stays entirely within our property line"],
     "closing": "thank you for the quick review", "sig": "the nguyens"},
    {"greeting": "hey dad", "paras": [
        "flights are booked, we land friday at 4:40 on the delta flight, and yes we'll text when we board like always",
        "don't cook anything big friday night, we're taking you two out for the anniversary, it's booked and it's a surprise so don't ask questions"],
     "closing": "see you soon", "sig": "your favorite child"},
    {"greeting": "hi lauren", "paras": [
        "the printer proofs came in and the colors look great, but the back cover text is noticeably smaller than the mockup, I put both under the same light and photographed them side by side",
        "can we get one more proof with the back text bumped up before we approve the run, I'd rather lose two days now than live with it on 500 copies"],
     "closing": "thanks", "sig": "sam"},
    {"greeting": "hello ms park", "paras": [
        "this is dana from the tuesday evening pottery class, I need to miss the next two sessions for work travel",
        "is there a makeup option in another section, and if not, could my glaze pieces stay on the shelf until I'm back on the 28th"],
     "closing": "thank you", "sig": "dana"},
    {"greeting": "hi all", "paras": [
        "the fantasy league is back, buy in stays at 25, draft is sunday september 1st at 7pm at my place, pizza provided, hot takes mandatory",
        "new this year, last place buys the trophy winner's dinner at the diner, wear that shame proudly kevin"],
     "closing": "reply to confirm your spot", "sig": "commissioner liam"},
    {"greeting": "hi property management", "paras": [
        "the hallway light on the second floor of building c has been out for over a week, it's the one right by the stairwell door, and it's genuinely dark there at night",
        "someone's going to miss a step eventually, could this get bumped up the maintenance list"],
     "closing": "thanks", "sig": "unit 2c"},
    {"greeting": "hey band", "paras": [
        "the open mic on the 15th has a slot at 8:40, twenty minutes, which fits the four song set if we keep the banter under control, looking at you steve",
        "load in is 7:30 through the alley door, they provide the drum kit so we just bring breakables and cables"],
     "closing": "practice thursday as usual", "sig": "ava"},
    {"greeting": "hi ms rodriguez", "paras": [
        "thank you for the offer letter, I'm excited about the role and plan to accept, I just have two questions before signing",
        "first, does the relocation stipend get paid up front or reimbursed, and second, can the start date shift one week later to october 6th so I can give proper notice"],
     "closing": "best", "sig": "hannah cole"},
    {"greeting": "hello city parks department", "paras": [
        "our running club would like to reserve the pavilion at eastside park for saturday october 11th, 8am to noon, expecting about 40 people",
        "we did the same event last year under the name lakeside striders, happy to fill out whatever form this needs, and we always leave the place cleaner than we found it"],
     "closing": "thank you", "sig": "raj patel, club coordinator"},
    {"greeting": "hi grandma", "paras": [
        "it's yuki, mom set up this email on your tablet so we can send pictures, the grandkids drew you something and it's attached, the purple blob is apparently you, take it as a compliment",
        "we'll call sunday after lunch like always, and dad says to remind you the doctor moved to the new building across from the bakery"],
     "closing": "love you lots", "sig": "yuki and the kids"},
]

# Meeting-note dumps: intro, optional leading prose, labeled list sections,
# optional trailing prose.
MEETINGS = [
    {"intro": "notes from monday standup", "pre": "short one today since half the team is at the conference.",
     "sections": [{"label": "blockers", "items": ["the auth service cert expires wednesday and nobody owns the renewal", "design review for the export flow still isn't scheduled"]},
                  {"label": "action items", "items": ["priya renews the cert today", "I'll grab a design slot for thursday", "marcus updates the release notes draft"]}],
     "post": "next standup is wednesday, same time."},
    {"intro": "sprint retro summary", "pre": "overall mood was good, velocity was fine, the mid sprint scope change was the main sore spot.",
     "sections": [{"label": "went well", "items": ["the pairing rotation, everyone wants to keep it", "zero rollbacks this sprint", "support tickets down 20 percent after the fix"]},
                  {"label": "needs work", "items": ["scope changes need a written note, not a hallway ask", "staging data is stale and it burned us twice"]}],
     "post": "I'll bring a staging refresh proposal to planning."},
    {"intro": "notes from the client kickoff with meridian", "pre": "friendly call, they know what they want, timeline is aggressive but not crazy.",
     "sections": [{"label": "decisions", "items": ["phase one is the dashboard only, mobile waits", "their team handles data cleanup before we touch it", "weekly checkins on thursdays at 10"]},
                  {"label": "action items", "items": ["send the sow revision by friday", "they send sample data monday", "we name a backup contact on each side"]}],
     "post": "watch out, their cto mentioned a hard board demo on the 30th, that date is real."},
    {"intro": "pta meeting recap for those who escaped", "pre": None,
     "sections": [{"label": "decided", "items": ["the fall fundraiser is the silent auction again, not the wrapping paper thing", "budget for teacher appreciation week goes up to 600", "the book fair moves to the gym this year"]},
                  {"label": "volunteers needed", "items": ["two people for auction item pickup", "someone with a truck for the book fair shelves", "a treasurer in training since gail retires in june"]}],
     "post": "next meeting is november 5th in the library, childcare provided this time."},
    {"intro": "band meeting notes, kitchen edition", "pre": "attendance, all four of us plus dave's dog.",
     "sections": [{"label": "agreed", "items": ["the ep is five songs, not seven, we cut the two nobody fights for", "recording happens over two weekends in february", "the name change discussion is officially dead, we stay velvet antler"]},
                  {"label": "todo", "items": ["ben books the studio deposit", "ava drafts the cover art brief", "steve finally replaces the amp fuse instead of the ritual"]}],
     "post": None},
    {"intro": "notes from the vendor comparison call", "pre": "we walked both quotes with procurement on the line.",
     "sections": [{"label": "where they differ", "items": ["northstar includes migration labor, kestrel bills it hourly", "kestrel's support sla is four hours versus next business day", "northstar wants a three year term for the good price"]},
                  {"label": "next steps", "items": ["ask northstar for a two year term at the same rate", "get kestrel's migration estimate in writing", "legal reviews both msas in parallel"]}],
     "post": "decision target is the 15th so implementation can start before the freeze."},
    {"intro": "family logistics summit, sunday dinner edition", "pre": None,
     "sections": [{"label": "settled", "items": ["thanksgiving is at our place, mom brings the pies", "the beach house deposit gets split four ways", "grandpa's 80th is a lunch party, he was consulted and prefers lunch"]},
                  {"label": "open questions", "items": ["who takes the dogs during the beach week", "whether the cousins are driving or flying"]}],
     "post": "revisit the open ones in the group chat by friday."},
    {"intro": "research group meeting notes", "pre": "professor okonkwo was traveling so lena ran it.",
     "sections": [{"label": "updates", "items": ["the sensor batch finally shipped, eta thursday", "the grant report draft is at 80 percent", "two undergrads joined for the semester, intros next week"]},
                  {"label": "action items", "items": ["everyone sends figure drafts to lena by monday", "I calibrate the new sensors when they land", "raj books the poster printing before the rush"]}],
     "post": None},
    {"intro": "notes from the budget sync with finance", "pre": "shorter than feared, numbers mostly behave.",
     "sections": [{"label": "the headlines", "items": ["travel is 40 percent under, reallocate or lose it in q4", "cloud spend crept up 8 percent, mostly the logging bill", "headcount budget survives the reforecast untouched"]},
                  {"label": "actions", "items": ["I propose the travel reallocation by wednesday", "devops audits log retention this sprint", "finance sends the updated template friday"]}],
     "post": "next sync moves to the first tuesday of the month going forward."},
    {"intro": "kitchen renovation planning call with the contractor", "pre": "walked the space on video, he measured twice, good sign.",
     "sections": [{"label": "locked in", "items": ["demo starts the 6th, one week after cabinets arrive", "the wall stays, it's load bearing and the beam cost is silly", "appliances get delivered to the garage the week before"]},
                  {"label": "we still owe him", "items": ["final faucet selection by friday", "the paint codes for the two walls", "a decision on under cabinet lighting"]}],
     "post": "total timeline is four weeks, which we are told to hear as five."},
    {"intro": "notes from the incident review", "pre": "blameless, focused, done in 40 minutes.",
     "sections": [{"label": "what we know", "items": ["the queue backed up for 90 minutes before the alert fired", "the alert threshold was set for last year's traffic", "manual drain worked exactly as the runbook said"]},
                  {"label": "follow ups", "items": ["thresholds move to percentage based this week", "the runbook gets a link from the alert itself", "we add a synthetic canary for the queue path"]}],
     "post": "owners and dates are in the tracker, review closes friday."},
    {"intro": "wedding planning checkpoint, t minus five months", "pre": None,
     "sections": [{"label": "done", "items": ["venue paid through the second installment", "band booked, contract signed", "guest list frozen at 112, no take backs"]},
                  {"label": "this month", "items": ["tasting on the 9th, bring opinions and stretchy pants", "book the hotel block before the conference eats the rooms", "order invitations, addressed by hand per the family tradition"]}],
     "post": "the spreadsheet remains the single source of truth, feelings may be filed as comments."},
    {"intro": "coop board meeting summary", "pre": "quorum reached with one proxy.",
     "sections": [{"label": "votes", "items": ["roof assessment approved, 8 to 1, spread over six months", "bike room key deposit drops to 25 dollars", "the lobby art stays, democracy has spoken"]},
                  {"label": "maintenance queue", "items": ["boiler service scheduled for the 12th", "the garage door sensor is on order", "hallway repainting moves to spring"]}],
     "post": "minutes will be posted by the elevator and ignored as usual."},
    {"intro": "notes from the accessibility audit readout", "pre": "external auditors, thorough, refreshingly unsalesy.",
     "sections": [{"label": "critical findings", "items": ["checkout is not keyboard navigable past the address step", "error states rely on color alone", "the pdf statements are unreadable by screen readers"]},
                  {"label": "quick wins", "items": ["focus indicators are one css change", "alt text is missing on exactly 41 images, list provided", "the contrast fixes are all in two design tokens"]}],
     "post": "we committed to the criticals within 60 days, it's in the letter."},
    {"intro": "sunday league captains meeting", "pre": None,
     "sections": [{"label": "rule changes this season", "items": ["rolling subs instead of quarters", "the offside experiment is dead, back to normal", "yellow card fine goes to the pizza fund"]},
                  {"label": "schedule notes", "items": ["season opens april 6th", "the field by the water tower is closed until may", "playoffs are single elimination, top six"]}],
     "post": "captains owe rosters by march 25th, no roster no schedule."},
    {"intro": "notes from the museum volunteer orientation", "pre": "two hours, half of it useful, summarizing the useful half.",
     "sections": [{"label": "the rules that matter", "items": ["never touch the art, even to save it, call security instead", "school groups always have right of way in the galleries", "the staff door code changes monthly, it's in the volunteer portal"]},
                  {"label": "my schedule", "items": ["saturdays 10 to 2 in the modern wing", "first wednesday evenings for members events", "the docent training track starts in january if I want it"]}],
     "post": None},
    {"intro": "product council notes, october session", "pre": "three pitches, one hour, decisions actually got made for once.",
     "sections": [{"label": "greenlit", "items": ["the bulk edit feature, scoped to tables only", "the api usage dashboard customers keep asking for"]},
                  {"label": "parked", "items": ["the ai summary thing, revisit when costs drop", "the theming system, no owner, no pitch, no thanks"]}],
     "post": "greenlit items get prds within two weeks, council reviews again november 12th."},
    {"intro": "notes from the call with the estate lawyer", "pre": "dad joined by phone, all questions welcome, she was patient.",
     "sections": [{"label": "what we're doing", "items": ["updating both wills, last touched 2009", "adding a healthcare proxy for each parent", "moving the house into the trust"]},
                  {"label": "documents to gather", "items": ["the current deed", "account statements, one per institution", "the life insurance policy numbers"]}],
     "post": "next appointment is november 3rd, documents due to her office a week before."},
    {"intro": "climbing gym committee notes", "pre": None,
     "sections": [{"label": "agreed", "items": ["the comp is february 8th, registration caps at 120", "route setting closes the north wall the week before", "volunteer judges get a free month"]},
                  {"label": "todo", "items": ["nina drafts the sponsorship email", "carlos prices the tshirts, two vendors", "I book the photographer from last year"]}],
     "post": "next meeting after thursday setting, same corner table."},
    {"intro": "notes from the parent coach sync", "pre": "quick call about the tournament weekend.",
     "sections": [{"label": "logistics", "items": ["first game saturday 9am, arrive 8:15", "carpool leaves the school lot at 7:30", "hotel block is under the club name, cutoff wednesday"]},
                  {"label": "kids need", "items": ["both jerseys, they check colors at the gate", "packed lunch saturday, the venue food line is an hour", "signed medical forms if they're not on file"]}],
     "post": "rain plan is the indoor facility, decision by friday 6pm on the team app."},
    {"intro": "quarterly review prep meeting notes", "pre": "we agreed on the story before touching slides, progress.",
     "sections": [{"label": "the narrative", "items": ["retention is the headline, up 6 points", "the miss on new logos gets owned early, no burying it", "the ask is two backend heads and the data contract renewal"]},
                  {"label": "who does what", "items": ["I write the one pager by tuesday", "dana builds the retention deep dive", "marcus dry runs the demo against the guest wifi, lesson learned"]}],
     "post": "full rehearsal thursday 3pm, the real thing is monday 9am sharp."},
    {"intro": "notes from the seed library planning meeting", "pre": None,
     "sections": [{"label": "decisions", "items": ["launch at the spring fair, april 12th", "the catalog starts with 30 varieties, all donated", "checkout is honor system with a paper log, we are not building an app, gary"]},
                  {"label": "needs before launch", "items": ["envelopes, a thousand, the small coin kind", "a donated filing cabinet, two drawer minimum", "three volunteers for the fair table"]}],
     "post": "the library said yes to the corner by the maps, which is prime real estate."},
]

# "new line" command pieces: each line is spoken separated by a new line command.
NEWLINE_PIECES = [
    {"lines": ["ship to jamie rivera", "482 maple court", "apartment 3b", "portland oregon 97214"]},
    {"lines": ["wifi network is casa verde", "password is sunflower 4482", "guest network is casa verde guest, same password"]},
    {"lines": ["dinner reservation details", "saturday the 14th at 7:30", "party of five under nguyen", "patio side if weather holds"]},
    {"lines": ["gift tag should say", "happy retirement joyce", "from all of us on the fourth floor", "don't spend it all on yarn"]},
    {"lines": ["the sign for the stand should read", "fresh eggs 5 dollars a dozen", "honor box on the left", "knock if we're home"]},
    {"lines": ["emergency contacts for the sitter", "us, the cell numbers on the fridge", "dr patel's office 555 0119", "poison control 800 222 1222", "neighbor rita next door, blue house"]},
    {"lines": ["for the trophy engraving", "first place mixed doubles", "riverside open 2026", "kim and delgado"]},
    {"lines": ["return address is", "the whitfield family", "17 candlewood lane", "burlington vermont 05401"]},
    {"lines": ["locker combo for the gym", "14 left", "32 right", "6 left", "lift the latch while turning the last one"]},
    {"lines": ["whiteboard message for the crew", "inspection is thursday", "clear the loading dock wednesday night", "coffee's on me friday if we pass"]},
    {"lines": ["the plaque should say", "in memory of walter greene", "who fed every stray on harbor street", "1941 to 2025"]},
    {"lines": ["cake order details", "chocolate with raspberry filling", "serves 30", "writing says congrats dr morales", "pickup friday at noon under the name kim"]},
    {"lines": ["put on the calendar", "recycling goes out wednesday night now", "green bin week alternates", "big pickup is the first monday of the month"]},
    {"lines": ["the label for the box is", "winter gear, gloves hats and the good scarves", "attic, left side", "open me in november"]},
    {"lines": ["voicemail greeting should be", "you've reached the front desk at harborview dental", "we're with patients or away from the phone", "leave a message and we'll call back within the hour"]},
]

# "bullet point" command pieces: speaker literally says the bullet command.
BULLET_CMD_PIECES = [
    {"intro": "agenda for the one on one", "items": ["the promotion timeline conversation", "feedback on the last launch", "conference budget for next year"]},
    {"intro": "questions for the landlord before we sign", "items": ["is the basement included in the square footage", "who handles snow removal", "what did the last tenants pay for heat in january"]},
    {"intro": "topics for the podcast intro", "items": ["the studio move", "listener mail about episode 40", "the guest's new book"]},
    {"intro": "things to ask the doctor", "items": ["whether the dosage change explains the headaches", "if the generic version is actually identical", "when we can retest the levels"]},
    {"intro": "notes for the babysitter", "items": ["dinner is in the fridge, just microwave it", "bedtime is 8 but 8:30 is survivable", "the cat is not allowed outside no matter what he tells you"]},
    {"intro": "what to cover in the demo", "items": ["the import flow start to finish", "the new keyboard shortcuts", "the export everyone has been begging for"]},
    {"intro": "packing for the day hike", "items": ["three liters of water minimum", "the first aid kit from the garage shelf", "rain shells even though the forecast is lying about sunshine", "the paper map because the canyon eats phone signal"]},
    {"intro": "for the yard sale ad", "items": ["furniture from a smoke free home", "kids bikes, two sizes", "kitchen everything, seriously everything", "saturday 8 to 1, early birds pay double"]},
    {"intro": "improvements for next quarter", "items": ["cut the standup to 15 minutes for real this time", "one demo friday a month", "rotate who runs planning so it's not always me"]},
    {"intro": "reasons we're switching vets, for the review", "items": ["three weeks for a sick visit is not a sick visit", "prices went up twice in a year", "the new place does home visits for seniors"]},
    {"intro": "stuff for the sublet listing", "items": ["furnished one bedroom, june through august", "utilities included except electric", "no pets because of the building not because of us", "two blocks from the green line"]},
    {"intro": "talking points for the budget call", "items": ["the overage is entirely the venue change", "we saved 12 percent on printing", "next year needs a contingency line so this meeting stops happening"]},
    {"intro": "rules for game night", "items": ["phones in the basket", "winner picks the next game", "loser does the dishes", "no monopoly, this is a peace treaty not a suggestion"]},
    {"intro": "what the plumber should look at", "items": ["the upstairs shower pressure", "the hammering sound when the washer fills", "the outdoor spigot that never fully closes"]},
    {"intro": "feedback for the caterer", "items": ["the passed appetizers were the hit of the night", "vegetarian mains ran out an hour in", "the staff was fantastic, especially whoever saved the cake situation"]},
    {"intro": "goals for the mentorship program", "items": ["every mentee ships something public in six months", "mentors get real training not a pdf", "we measure outcomes, not meetings held"]},
    {"intro": "for the moving company quote", "items": ["two bedroom apartment, third floor with elevator", "one upright piano, the deal breaker item", "moving date is flexible within the last week of may"]},
    {"intro": "requirements for the new laptop", "items": ["32 gigs of ram, non negotiable", "actual function keys", "battery that survives a cross country flight", "under 2,000 all in"]},
]
