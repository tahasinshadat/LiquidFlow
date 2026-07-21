# banks_core.py
# Content banks for the LiquidFlow dictation-reformatter dataset.
# Item pools, task pools, list frames, step sequences, retraction units, and
# clean base sentences. All content is authored; generators in build_dataset.py
# compose (raw, formatted) pairs from these banks so that the formatted side is
# correct by construction (wording identical, structure only).

# ---------------------------------------------------------------------------
# List item pools (things people dictate as linear enumerations)
# ---------------------------------------------------------------------------

GROCERY_ITEMS = [
    "eggs", "whole milk", "oat milk", "sourdough bread", "bagels", "butter",
    "cream cheese", "greek yogurt", "cheddar", "shredded mozzarella",
    "parmesan", "ground turkey", "chicken thighs", "salmon", "bacon",
    "rotisserie chicken", "tofu", "black beans", "chickpeas", "jasmine rice",
    "brown rice", "spaghetti", "penne", "marinara sauce", "pesto",
    "olive oil", "soy sauce", "sriracha", "peanut butter", "strawberry jam",
    "honey", "maple syrup", "oatmeal", "granola", "cheerios", "flour",
    "sugar", "brown sugar", "baking soda", "vanilla extract",
    "chocolate chips", "bananas", "a bag of oranges", "apples", "blueberries",
    "strawberries", "lemons", "limes", "avocados", "baby spinach", "kale",
    "romaine", "cherry tomatoes", "cucumbers", "bell peppers", "red onions",
    "garlic", "ginger", "carrots", "celery", "broccoli", "cauliflower",
    "sweet potatoes", "russet potatoes", "mushrooms", "green onions",
    "cilantro", "basil", "frozen peas", "frozen dumplings", "frozen pizza",
    "vanilla ice cream", "tortilla chips", "salsa", "hummus", "pita bread",
    "tortillas", "sparkling water", "cold brew", "orange juice",
    "coffee beans", "green tea", "paper towels", "toilet paper",
    "dish soap", "laundry detergent", "trash bags", "sponges",
    "aluminum foil", "ziploc bags", "cat litter", "dog food",
    "two pounds of ground beef", "a dozen eggs", "three cans of tuna",
]

HARDWARE_ITEMS = [
    "duct tape", "wood glue", "a box of drywall screws", "sandpaper",
    "painters tape", "a quart of white paint", "paint rollers", "wd-40",
    "zip ties", "picture hooks", "a stud finder", "wall anchors",
    "light bulbs", "an extension cord", "batteries", "a caulk gun",
    "silicone caulk", "work gloves", "a tape measure", "a level",
    "furnace filters", "weather stripping",
]

PHARMACY_ITEMS = [
    "ibuprofen", "allergy meds", "band aids", "sunscreen", "vitamin d",
    "melatonin", "cough drops", "nasal spray", "hand sanitizer",
    "contact solution", "floss", "toothpaste", "a new toothbrush",
    "shampoo", "razor blades", "electrolyte packets",
]

PACKING_ITEMS = [
    "passport", "phone charger", "portable battery", "noise canceling headphones",
    "a rain jacket", "hiking boots", "swimsuit", "sunglasses", "toiletries bag",
    "melatonin for the flight", "compression socks", "a travel pillow",
    "laptop and charger", "the camera", "extra sd cards", "a power adapter",
    "running shoes", "a light sweater", "the itinerary printout",
    "snacks for the plane", "an empty water bottle", "the parking pass",
]

PARTY_ITEMS = [
    "balloons", "streamers", "a banner", "paper plates", "plastic cups",
    "napkins", "a veggie tray", "chips and guac", "sliders", "lemonade",
    "a cooler of ice", "candles for the cake", "goodie bags",
    "a bluetooth speaker", "folding chairs", "sparklers",
]

# ---------------------------------------------------------------------------
# Task pools (todo lists)
# ---------------------------------------------------------------------------

WORK_TASKS = [
    "reply to the vendor email", "finish the q3 slide deck",
    "review priya's pull request", "book the offsite venue",
    "update the onboarding doc", "send the invoice to accounting",
    "schedule the design review", "close out the stale jira tickets",
    "prep talking points for the all hands", "follow up with legal on the msa",
    "rotate the api keys", "merge the release branch",
    "write up the incident postmortem", "submit my expense report",
    "share the roadmap draft with marketing", "set up the new intern's laptop",
    "cancel the duplicate zoom license", "reschedule the 1 on 1 with dana",
    "post the job listing for the backend role", "test the staging deploy",
    "archive the old slack channels", "update my ooo calendar for august",
    "ping mike about the contract renewal", "draft the customer apology email",
    "clean up the shared drive", "double check the payroll cutoff date",
    "get sign off from finance", "move the retro to thursday",
    "file the trademark paperwork", "send sarah the final figures",
    "upload the demo recording", "renew the ssl cert before friday",
    "triage the support backlog", "confirm headcount for the workshop",
    "add captions to the launch video", "order new badges for the office",
    "back up the analytics dashboard", "kill the flaky nightly job",
    "collect feedback from the beta group", "print handouts for the client visit",
]

HOME_TASKS = [
    "take out the recycling", "water the tomatoes", "fix the leaky faucet",
    "vacuum the stairs", "clean out the fridge", "mow the lawn",
    "swap the furnace filter", "drop off the dry cleaning",
    "return the amazon package", "hang the new shelves",
    "descale the coffee machine", "wash the car", "clean the gutters",
    "organize the garage", "pay the water bill", "schedule the chimney sweep",
    "flip the mattress", "reseed the bare patch in the yard",
    "put away the winter coats", "label the storage bins",
    "test the smoke detectors", "wipe down the baseboards",
    "call the plumber about the water heater", "donate the old bookshelf",
    "refill the bird feeder", "set up the new router",
    "deep clean the oven", "patch the hole in the drywall",
    "book the dog's grooming appointment", "defrost the chest freezer",
    "change the hvac filter", "clean the dryer vent",
    "sharpen the kitchen knives", "repot the monstera", "seal the deck",
]

SCHOOL_TASKS = [
    "finish the stats problem set", "email professor lin about the extension",
    "start the lit review for my thesis", "print the lab report",
    "register for fall classes", "return the library books",
    "study for the orgo midterm", "record the group presentation",
    "submit the fafsa renewal", "book a tutoring slot for calc",
    "outline the history essay", "upload the code for assignment 4",
    "get the textbook from the bookstore", "review the flashcards for spanish",
    "sign up for the career fair", "meet with my advisor about credits",
    "revise the personal statement", "finish the peer reviews",
    "pay the lab fee", "practice the debate rebuttal",
    "scan my notes for the study group", "request my transcript",
]

FITNESS_TASKS = [
    "do the mobility routine", "hit legs at the gym", "run an easy 5k",
    "meal prep chicken and rice for the week", "stretch my hamstrings",
    "book a physio appointment for my shoulder", "swim laps before work",
    "log my macros", "deload on bench this week", "foam roll after the run",
    "sign up for the october half marathon", "replace my worn out running shoes",
    "do 20 minutes on the bike", "pack my gym bag tonight",
]

SIDEPROJECT_TASKS = [
    "fix the dark mode toggle", "write the readme for the plugin",
    "deploy the landing page", "set up stripe test mode",
    "add rate limiting to the api", "sketch the onboarding flow",
    "migrate the database to postgres", "record a 60 second demo video",
    "buy the domain before someone else does", "respond to the github issues",
    "add error logging to the worker", "clean up the css variables",
    "write three blog post drafts", "wire up the waitlist form",
    "profile the slow query", "bump the dependencies",
    "add unit tests for the parser", "design the app icon",
    "publish the beta to testflight", "email the first ten signups",
]

# ---------------------------------------------------------------------------
# Frames for list examples. "intro" frames go before the items, "outro" frames
# go after. Text is spoken exactly as written; casing variants applied later.
# ---------------------------------------------------------------------------

LIST_FRAMES = {
    "grocery": {
        "intro": [
            "okay shopping list for the week",
            "grocery run after work, I need",
            "add these to the grocery list",
            "stuff to grab at trader joe's",
            "for the costco trip we need",
            "things we're out of",
            "shopping list before the weekend",
            "picking up groceries later, don't let me forget",
            "on the list for sunday meal prep",
            "quick store run, I need",
        ],
        "outro": [
            "that should cover the week",
            "and that's everything for the fridge",
            "I think that's the whole list",
        ],
    },
    "hardware": {
        "intro": [
            "home depot list for the shelf project",
            "hardware store run this weekend, we need",
            "for fixing up the bathroom I need",
            "stuff for the garage project",
        ],
        "outro": [
            "should be one trip if I'm lucky",
        ],
    },
    "pharmacy": {
        "intro": [
            "pharmacy stuff while you're out",
            "can you grab from cvs",
            "drugstore list for the trip",
        ],
        "outro": [
            "thanks, that's everything",
        ],
    },
    "packing": {
        "intro": [
            "packing list for the portland trip",
            "before we leave thursday I need to pack",
            "don't let me forget to pack",
            "suitcase checklist for japan",
        ],
        "outro": [
            "everything else I can buy there",
        ],
    },
    "party": {
        "intro": [
            "for maya's birthday party we still need",
            "party supplies to pick up saturday morning",
            "cookout list for the fourth",
        ],
        "outro": [
            "I'll grab the cake separately",
        ],
    },
    "work": {
        "intro": [
            "okay todos for today",
            "things I have to get done before friday",
            "my plate for this week",
            "before the offsite I need to",
            "top of mind for tomorrow",
            "stuff I keep putting off, today's the day",
            "quick list before I forget",
        ],
        "outro": [
            "if I clear those I'm calling it a win",
            "everything else can slide to next week",
        ],
    },
    "home": {
        "intro": [
            "house stuff for the weekend",
            "chores I have to knock out saturday",
            "before my parents visit I need to",
            "home todos, in no particular order",
        ],
        "outro": [
            "then I can actually relax on sunday",
        ],
    },
    "school": {
        "intro": [
            "school stuff due this week",
            "assignments hanging over my head",
            "before finals week I have to",
        ],
        "outro": [
            "and then I'm free for the summer",
        ],
    },
    "fitness": {
        "intro": [
            "training todos for the week",
            "gym stuff I've been slacking on",
        ],
        "outro": [
            "consistency over intensity, allegedly",
        ],
    },
    "sideproject": {
        "intro": [
            "app stuff for this weekend",
            "before I show anyone the beta I need to",
            "side project backlog, the short version",
        ],
        "outro": [
            "ship it ugly, fix it later",
        ],
    },
}

LIST_POOLS = {
    "grocery": GROCERY_ITEMS,
    "hardware": HARDWARE_ITEMS,
    "pharmacy": PHARMACY_ITEMS,
    "packing": PACKING_ITEMS,
    "party": PARTY_ITEMS,
    "work": WORK_TASKS,
    "home": HOME_TASKS,
    "school": SCHOOL_TASKS,
    "fitness": FITNESS_TASKS,
    "sideproject": SIDEPROJECT_TASKS,
}

# ---------------------------------------------------------------------------
# Step sequences (ordered instructions -> numbered lists).
# Each dict: intro variants (may include None for no intro), steps in order.
# Step wording never begins with a connective; connectives are added by the
# generator when linearizing and removed in the formatted output.
# ---------------------------------------------------------------------------

STEP_SEQUENCES = [
    {"domain": "cooking", "intros": ["here's how I make cold brew", "cold brew recipe for the office"], "steps": [
        "grind a cup of beans nice and coarse",
        "add them to the big mason jar with four cups of cold water",
        "stir it once and put the lid on",
        "let it sit in the fridge for 16 hours",
        "strain it through the mesh filter twice",
        "dilute one to one with water or milk when you serve it"]},
    {"domain": "cooking", "intros": ["the pancake routine goes like this", None], "steps": [
        "whisk two eggs with a cup of buttermilk",
        "fold in the dry mix until it's just combined, lumps are fine",
        "let the batter rest for ten minutes",
        "cook on medium until the bubbles pop and stay open",
        "flip once and give it another minute"]},
    {"domain": "cooking", "intros": ["for the salmon marinade"], "steps": [
        "mix soy sauce, honey, grated ginger, and a splash of rice vinegar",
        "coat the fillets and let them sit for 20 minutes, no longer",
        "roast at 425 for about 11 minutes",
        "spoon the leftover glaze on top before serving"]},
    {"domain": "cooking", "intros": ["grandma's rice method, writing it down so I stop guessing"], "steps": [
        "rinse the rice until the water runs clear",
        "use one and a half cups of water per cup of rice",
        "bring it to a boil uncovered",
        "drop it to the lowest heat and cover for 12 minutes",
        "leave the lid on for another ten, don't peek"]},
    {"domain": "cooking", "intros": ["how we do the sunday sauce"], "steps": [
        "brown the sausage in the dutch oven and set it aside",
        "sweat the onions and garlic in the same pot",
        "add two cans of crushed tomatoes and a parmesan rind",
        "put the sausage back in and simmer low for three hours",
        "taste at the end, it usually needs a pinch of sugar"]},
    {"domain": "cooking", "intros": ["sourdough feeding schedule", None], "steps": [
        "discard down to 50 grams of starter",
        "feed it 50 grams of flour and 50 of water",
        "leave it on the counter until it doubles",
        "stick it back in the fridge if I'm not baking"]},
    {"domain": "cooking", "intros": ["the smash burger process for saturday"], "steps": [
        "roll the beef into loose 3 ounce balls",
        "get the griddle screaming hot",
        "smash each ball flat with the press for ten seconds",
        "season, flip when the edges go crispy",
        "cheese on, stack them, toast the buns in the fat"]},
    {"domain": "tech", "intros": ["to deploy the site", "deploy steps so I stop asking mike"], "steps": [
        "merge to main and wait for ci to go green",
        "run the release script with the version bump flag",
        "check the staging url for anything broken",
        "promote staging to prod from the dashboard",
        "watch the error tracker for ten minutes"]},
    {"domain": "tech", "intros": ["setting up the dev environment on a new laptop"], "steps": [
        "clone the repo and copy env.example to env.local",
        "install node 20 with nvm",
        "run pnpm install from the root",
        "start docker desktop and run the compose file",
        "seed the database with the seed script",
        "open localhost 3000 and log in with the test account"]},
    {"domain": "tech", "intros": ["how to rotate the api keys without downtime"], "steps": [
        "generate the new key in the vendor dashboard",
        "add it to the secrets manager under a v2 name",
        "deploy the config change that reads both keys",
        "flip the traffic flag to the new key",
        "revoke the old key after 24 hours"]},
    {"domain": "tech", "intros": ["getting the printer to behave again", None], "steps": [
        "turn it off and unplug it for 30 seconds",
        "hold the wifi button until the light blinks",
        "rejoin it to the network from the app",
        "print the test page before you trust it"]},
    {"domain": "tech", "intros": ["fixing a broken migration locally"], "steps": [
        "roll back with the down command",
        "delete the bad row from the migrations table",
        "fix the sql in the migration file",
        "run it again and diff the schema against staging"]},
    {"domain": "tech", "intros": ["backup routine for the nas"], "steps": [
        "snapshot the volumes friday night",
        "sync the snapshot to the cloud bucket",
        "verify the checksum report saturday morning",
        "prune anything older than 90 days"]},
    {"domain": "tech", "intros": ["onboarding a new engineer, the short version"], "steps": [
        "send the laptop and accounts request a week early",
        "pair them with a buddy for the first sprint",
        "have them ship a tiny fix on day two",
        "book the architecture walkthrough for week one",
        "check in at the 30 day mark with real questions"]},
    {"domain": "tech", "intros": ["how I triage a pager alert"], "steps": [
        "acknowledge it so it stops escalating",
        "check the dashboard for the blast radius",
        "roll back the last deploy if it lines up",
        "open an incident channel if customers are affected",
        "write the timeline down while it's fresh"]},
    {"domain": "household", "intros": ["patching the drywall in the hallway"], "steps": [
        "cut a clean square around the hole",
        "screw a backing strip behind the opening",
        "fit the patch piece and tape the seams",
        "mud it thin, three coats, sand between",
        "prime before you paint or it will flash"]},
    {"domain": "household", "intros": ["winterizing the sprinkler system"], "steps": [
        "shut off the main supply valve",
        "open the drain at the low point",
        "blow out each zone with the compressor at 50 psi",
        "leave the valves at 45 degrees for the season"]},
    {"domain": "household", "intros": ["deep cleaning the espresso machine"], "steps": [
        "backflush with the blind basket and cleaner",
        "soak the portafilter and baskets for 20 minutes",
        "descale the boiler with the citric solution",
        "run two full tanks of fresh water through",
        "pull a sacrifice shot and dump it"]},
    {"domain": "household", "intros": ["moving day plan for the big furniture"], "steps": [
        "take the legs off the couch first",
        "wrap the table top in the moving blankets",
        "stage everything in the garage by size",
        "load the heavy stuff against the cab wall",
        "strap each row before stacking boxes"]},
    {"domain": "household", "intros": ["jump starting the car without frying anything"], "steps": [
        "red clamp on the dead battery's positive post",
        "other red on the donor positive",
        "black on the donor negative",
        "last black clamp on bare metal, not the battery",
        "start the donor, wait two minutes, then try yours"]},
    {"domain": "fitness", "intros": ["warmup before heavy squats"], "steps": [
        "five minutes easy on the bike",
        "hip circles and leg swings, ten each side",
        "two sets of bodyweight squats to depth",
        "empty bar for eight slow reps",
        "add plates in 20 percent jumps to working weight"]},
    {"domain": "fitness", "intros": ["my friday swim set", None], "steps": [
        "400 easy freestyle to loosen up",
        "8 by 50 drill focusing on catch",
        "6 by 100 at threshold with 20 seconds rest",
        "200 backstroke cooldown"]},
    {"domain": "fitness", "intros": ["the knee rehab circuit from the physio"], "steps": [
        "banded side steps, 15 each way",
        "single leg bridges, three sets of ten",
        "step downs off the low box, slow on the way down",
        "wall sit for 45 seconds to finish"]},
    {"domain": "admin", "intros": ["renewing the passport, painful but simple"], "steps": [
        "fill out the ds 82 online and print it",
        "get the photo done at the pharmacy counter",
        "write the check for 130 dollars",
        "mail it with the old passport in a tracked envelope",
        "screenshot the tracking number somewhere findable"]},
    {"domain": "admin", "intros": ["submitting expenses so finance doesn't bounce it"], "steps": [
        "scan every receipt over 25 dollars",
        "tag each line with the project code",
        "attach the approval email from your manager",
        "submit before the 25th or it rolls a month"]},
    {"domain": "admin", "intros": ["how we run the book club without chaos"], "steps": [
        "vote on the next book in the group chat by friday",
        "whoever picked it hosts",
        "host sends three discussion questions the day before",
        "cap the wine talk at 30 minutes, then actual book talk"]},
    {"domain": "admin", "intros": ["disputing the parking ticket"], "steps": [
        "photograph the sign and the curb from where you parked",
        "file the online contest form within 21 days",
        "attach the photos and keep it to three sentences",
        "save the confirmation number in notes"]},
    {"domain": "admin", "intros": ["tax prep checklist before the accountant call"], "steps": [
        "download the w2 and both 1099s",
        "export the donation receipts from email",
        "total the home office square footage",
        "pull the property tax statement from the county site",
        "put it all in one folder named taxes 2026"]},
    {"domain": "cooking", "intros": ["pizza dough timeline for friday night pizza"], "steps": [
        "mix the dough wednesday night, just combine it",
        "cold ferment in the fridge for 48 hours",
        "ball it up friday at noon",
        "let the balls warm up on the counter for two hours",
        "stretch, top, and bake at max heat on the steel"]},
    {"domain": "tech", "intros": ["restoring a postgres backup without sweating"], "steps": [
        "spin up a scratch database first",
        "restore the dump into the scratch db",
        "spot check the three biggest tables",
        "rename scratch to prod inside a transaction",
        "keep the old db around for a week just in case"]},
    {"domain": "tech", "intros": ["how to file a decent bug report"], "steps": [
        "write the exact steps you took, numbered",
        "paste the error text, not a screenshot of text",
        "note your os, app version, and account type",
        "say what you expected versus what happened",
        "attach logs if the app has an export button"]},
    {"domain": "household", "intros": ["seasoning the cast iron properly"], "steps": [
        "scrub it back to bare metal with salt",
        "dry it on the burner until it smokes",
        "wipe on a rice grain amount of crisco",
        "bake it upside down at 450 for an hour",
        "repeat the oil and bake twice more"]},
    {"domain": "admin", "intros": ["prepping for the visa interview"], "steps": [
        "print the appointment confirmation and the ds 160 page",
        "bring the bank statements from the last three months",
        "get there 30 minutes early, no big bags allowed",
        "answer only what's asked, short and boring wins"]},
    {"domain": "fitness", "intros": ["race morning routine for the half"], "steps": [
        "oatmeal and coffee three hours before the gun",
        "sip electrolytes until 45 minutes out",
        "two mile shakeout with four strides",
        "gel at the start line, then every 35 minutes",
        "nothing new on race day, obviously"]},
    {"domain": "cooking", "intros": ["the guac that disappears at every party"], "steps": [
        "mash three ripe avocados with a fork, keep it chunky",
        "fold in minced red onion, cilantro, and one jalapeno",
        "squeeze two limes over it",
        "salt it more than feels reasonable",
        "press plastic wrap onto the surface so it doesn't brown"]},
    {"domain": "tech", "intros": ["moving my music library to the new laptop"], "steps": [
        "export the library file from the old machine",
        "copy the media folder to the external drive",
        "point the new app at the drive before first launch",
        "let it reindex overnight",
        "spot check playlists before wiping the old laptop"]},
    {"domain": "household", "intros": ["getting the garden beds ready for spring"], "steps": [
        "pull the winter weeds while the soil is damp",
        "top each bed with two inches of compost",
        "run the drip lines and check for splits",
        "start tomatoes and peppers inside six weeks before last frost",
        "direct sow the greens once nights stay above 45"]},
    {"domain": "admin", "intros": ["switching banks without missing a payment"], "steps": [
        "open the new account but keep the old one alive",
        "move the direct deposit first",
        "migrate autopays one statement cycle at a time",
        "leave 500 in the old account as a buffer",
        "close the old account after two clean months"]},
    {"domain": "tech", "intros": ["publishing the podcast episode"], "steps": [
        "bounce the final mix at negative 16 lufs",
        "write the show notes with timestamps",
        "upload to the host and schedule for 5 am tuesday",
        "queue the clips for social",
        "email the guest the live link when it drops"]},
    {"domain": "household", "intros": ["hanging the gallery wall straight for once"], "steps": [
        "trace every frame on kraft paper and cut them out",
        "tape the paper shapes to the wall and fuss until it looks right",
        "mark each hook point through the paper",
        "drill, hook, hang, done",
        "step back ten feet before you commit to the last two"]},
]

# ---------------------------------------------------------------------------
# Retraction units. raw = pre + wrong + MARKER + right + post, out = pre + right + post.
# Contexts are long enough that removing the retracted words keeps unique-word
# retention high. Punctuation belongs to the strings as written.
# ---------------------------------------------------------------------------

RETRACTIONS = [
    {"pre": "hey can you send the invoice over on", "wrong": "monday", "right": "tuesday", "post": "since accounting closes the books that afternoon"},
    {"pre": "let's do the team lunch at the", "wrong": "thai place", "right": "ramen spot", "post": "because half the group went to the other one last week"},
    {"pre": "the demo for the client is at", "wrong": "2pm", "right": "3pm", "post": "eastern, calendar invite coming shortly"},
    {"pre": "can you book the conference room on the", "wrong": "fourth floor", "right": "second floor", "post": "the projector upstairs has been busted for weeks"},
    {"pre": "tell the movers we're free", "wrong": "saturday", "right": "sunday", "post": "morning any time after eight"},
    {"pre": "the flight lands in denver at", "wrong": "10:40", "right": "11:15", "post": "so let's say curb pickup around 11:45 to be safe"},
    {"pre": "put the leftovers budget line under", "wrong": "marketing", "right": "operations", "post": "and flag it for dana when she reviews the sheet"},
    {"pre": "we should paint the office", "wrong": "gray", "right": "the warm white", "post": "from the sample card, the third swatch down"},
    {"pre": "text grandma that we'll visit on", "wrong": "thursday", "right": "friday", "post": "afternoon once the kids are out of school"},
    {"pre": "the dentist rescheduled me to the", "wrong": "12th", "right": "19th", "post": "at 9:30 so I'll need the morning off"},
    {"pre": "order the cake from the bakery on", "wrong": "fifth street", "right": "grove street", "post": "the one with the pistachio thing we liked"},
    {"pre": "the rent check needs to go out by the", "wrong": "28th", "right": "26th", "post": "because the office is closed over the long weekend"},
    {"pre": "set the thermostat to", "wrong": "68", "right": "70", "post": "before my parents get here or we'll hear about it all night"},
    {"pre": "the recruiter call moved to", "wrong": "wednesday", "right": "thursday", "post": "at 4, same zoom link as before"},
    {"pre": "grab two bags of the", "wrong": "medium roast", "right": "dark roast", "post": "beans if the store still has them on sale"},
    {"pre": "the wifi password at the cabin is", "wrong": "lakeview12", "right": "lakeview21", "post": "all lowercase, it's on the fridge magnet too"},
    {"pre": "send the contract to her", "wrong": "gmail", "right": "work email", "post": "the legal team wants everything on the company domain"},
    {"pre": "we're presenting slides", "wrong": "10 through 14", "right": "10 through 16", "post": "so rehearse the pricing section too"},
    {"pre": "the reservation friday is under", "wrong": "my name", "right": "jordan's name", "post": "party of six at 7:30 on the patio side"},
    {"pre": "for the marathon I'm targeting a", "wrong": "4:10", "right": "4:05", "post": "finish which means 9:20 miles if the weather cooperates"},
    {"pre": "ship the replacement to the", "wrong": "office", "right": "apartment", "post": "since nobody will be at the front desk next week"},
    {"pre": "the baby shower is the", "wrong": "14th", "right": "21st", "post": "of june, they pushed it a week for venue reasons"},
    {"pre": "use the", "wrong": "blue", "right": "green", "post": "folder for the tax documents, the other one is medical stuff"},
    {"pre": "my car appointment at the mechanic is", "wrong": "8am", "right": "9am", "post": "so I can still make the standup if traffic behaves"},
    {"pre": "the recipe needs", "wrong": "two cups", "right": "two and a half cups", "post": "of flour or the dough comes out too sticky to shape"},
    {"pre": "point the domain at the", "wrong": "old server", "right": "new droplet", "post": "ip, the one ending in 84, before the ttl expires tonight"},
    {"pre": "tell the landlord the leak is in the", "wrong": "kitchen", "right": "bathroom", "post": "ceiling right above the fan, getting worse after rain"},
    {"pre": "the gym class I want is the", "wrong": "6am", "right": "7am", "post": "spin slot on tuesdays with the instructor everyone raves about"},
    {"pre": "book the rental from the", "wrong": "airport location", "right": "downtown location", "post": "it's 40 bucks cheaper and we land close to it anyway"},
    {"pre": "the code review comments are due", "wrong": "end of day", "right": "noon", "post": "tomorrow because the release train leaves at 2"},
    {"pre": "we owe the sitter", "wrong": "80", "right": "95", "post": "dollars because we came back late on friday night"},
    {"pre": "the hike saturday is the", "wrong": "lake loop", "right": "ridge trail", "post": "everyone voted and the views won over the shade"},
    {"pre": "set the meeting with the contractor for", "wrong": "tuesday morning", "right": "wednesday morning", "post": "he's finishing another job across town until then"},
    {"pre": "the prescription refill is ready at the", "wrong": "main street", "right": "elm street", "post": "pharmacy, they transferred it without telling anyone"},
    {"pre": "put the anniversary dinner deposit on the", "wrong": "visa", "right": "amex", "post": "so we get the points before the quarter closes"},
    {"pre": "the kids' recital starts at", "wrong": "5:30", "right": "6", "post": "doors at 5:15, and parking behind the auditorium fills fast"},
    {"pre": "quote the client", "wrong": "three weeks", "right": "four weeks", "post": "for delivery so we have slack if the vendor slips again"},
    {"pre": "the study group moved to the", "wrong": "library basement", "right": "third floor commons", "post": "because the basement rooms are booked for finals"},
    {"pre": "change the deploy window to", "wrong": "friday", "right": "monday", "post": "nobody wants to babysit prod over the weekend again"},
    {"pre": "grab my blazer from the", "wrong": "closet", "right": "dry cleaner", "post": "the ticket is in the car cupholder, should be ready after 4"},
]

# Clause-level restarts: speaker abandons a phrasing and restarts.
# raw = pre + false_start + MARKER + correct + post ; out = pre + correct + post
CLAUSE_RESTARTS = [
    {"pre": "about the offsite,", "false_start": "we could maybe try to", "correct": "let's just book the lake house again,", "post": "everyone liked it and the price hasn't changed since last year"},
    {"pre": "for dinner tonight", "false_start": "I was thinking we could do that", "correct": "let's keep it simple and do tacos,", "post": "we already have everything except the tortillas"},
    {"pre": "on the hiring thing,", "false_start": "I feel like we should probably", "correct": "we need to make a decision this week,", "post": "the candidate has another offer and she won't wait forever"},
    {"pre": "with the budget,", "false_start": "if we move the numbers around we might", "correct": "we can cover it from the travel line,", "post": "nobody is flying anywhere until the conference in november"},
    {"pre": "about the car,", "false_start": "I keep going back and forth on whether to", "correct": "we should just get the brakes done now,", "post": "the noise is getting worse and winter is coming and the shop has a loaner available this week"},
    {"pre": "for the newsletter,", "false_start": "maybe we could write something about", "correct": "let's lead with the customer story,", "post": "it's the strongest thing we have and it's already approved"},
    {"pre": "regarding the apartment,", "false_start": "we could see if the landlord would", "correct": "let's just renew for one year,", "post": "moving costs would eat any rent savings anyway"},
    {"pre": "on the api redesign,", "false_start": "part of me wants to rip out the whole", "correct": "we should version it and migrate slowly,", "post": "breaking the mobile clients again would be brutal and the app store review delay makes every mistake expensive"},
    {"pre": "for mom's birthday,", "false_start": "we could all chip in for a", "correct": "let's do the cooking class idea,", "post": "she mentioned it twice which for her is basically shouting"},
    {"pre": "about the gym membership,", "false_start": "I was going to see if pausing it", "correct": "just cancel it,", "post": "the home setup covers everything I actually do and the dues went up again in march anyway"},
    {"pre": "with the wedding photos,", "false_start": "we might want to wait until", "correct": "let's order the album now,", "post": "the discount code expires on the 30th"},
    {"pre": "for the standup,", "false_start": "maybe we should try moving it to", "correct": "let's keep the time and just cap it at ten minutes,", "post": "the problem isn't the hour it's the rambling and people visibly check out around minute twelve"},
    {"pre": "on the roadmap doc,", "false_start": "I could take another pass at the", "correct": "send it as is,", "post": "leadership wants direction not polish and we're already late"},
    {"pre": "about thanksgiving,", "false_start": "we were tossing around the idea of", "correct": "we're hosting,", "post": "I already told my sister so there's no backing out now and mom is planning the pie assignments as we speak"},
    {"pre": "for the intro course,", "false_start": "I thought about starting with", "correct": "start with the project based track,", "post": "people quit when the first month is all theory"},
]

# ---------------------------------------------------------------------------
# Clean base sentences: informal messages used for stutter injection and
# near-identity examples. Each reads naturally as dictated speech.
# ---------------------------------------------------------------------------

BASE_SENTENCES = [
    "hey are we still on for lunch tomorrow or should we push it to thursday",
    "the package finally showed up but the box looks like it went through a war",
    "can you forward me the slides from this morning's review when you get a chance",
    "I left my charger in the conference room, if anyone sees it can they drop it at my desk",
    "the landlord said the water will be off from 9 to noon on wednesday",
    "traffic on the bridge is horrible, I'll be maybe 15 minutes late",
    "the vet said luna's bloodwork came back normal which is a huge relief",
    "don't forget the parking garage closes at 11 on weeknights",
    "I think the printer on the third floor is out of toner again",
    "the client loved the mockups, especially the darker version of the homepage",
    "my sister is flying in friday night so I'll be offline most of the weekend",
    "we got the permit approved so the deck project is officially happening",
    "the coffee shop on grove street started doing those cardamom buns again",
    "quick heads up, the standup is moving to 9:45 starting next week",
    "the recruiter wants references by friday, can I put you down as one",
    "I finally beat that boss in elden ring after like forty tries",
    "the dryer is making that clunking noise again, third time this month",
    "someone parked in our spot again, silver suv, no note obviously",
    "the school called, jake has a low fever, nothing serious but he needs pickup",
    "the update went out to 10 percent of users and error rates look flat so far",
    "I'm thinking we repaint the hallway before we list the house",
    "the pharmacy says the refill won't be ready until after 3",
    "band practice moved to steve's garage this week because of the flooding",
    "can someone bring an hdmi cable to the 2 o'clock, the room only has usb c",
    "the tomatoes are finally turning red, we might actually get a harvest this year",
    "my flight got bumped to the 6:50 departure so dinner is off, rain check",
    "the deploy went clean, no rollbacks, cache warmed up in about four minutes",
    "our table at the trivia night is under the name bad decisions",
    "the insurance adjuster is coming tuesday between 10 and 2, someone has to be home",
    "I dropped the invite for the planning session, grab a slot if you care about the roadmap",
    "the babysitter can do saturday but needs to leave by 10:30",
    "the gym is closed for maintenance until monday so I'm running outside this week",
    "heads up the vpn cert expires tonight, reconnect if things look weird tomorrow",
    "grandpa's surgery went well and he's already complaining about the food, so, fully himself",
    "the neighbors are having a party saturday, they said we're welcome to crash it",
    "I moved the leftover pizza to the bottom shelf, label says friday on it",
    "the library extended the book, no late fee, we're safe until the 27th",
    "my phone screen has a crack now but honestly the case did its job for two years",
    "the interview panel wants a portfolio walkthrough instead of the coding round",
    "the hot water heater guy quoted 1,400 which feels steep, getting a second quote",
    "I signed us up for the 10k in september before I could talk myself out of it",
    "the wifi in the back office drops every day around 3, I swear it's the microwave",
    "the mechanic says it's just the belt, 200 bucks, way better than we feared",
    "the beta invite emails went out this morning, 62 signups by lunch",
    "reminder that monday is a holiday so payroll runs a day early",
    "the kid's soccer game got moved to the east field, same 9am start",
    "I found the receipt, we're inside the return window until the 14th",
    "the podcast hit 5,000 downloads this week which is bananas for episode four",
    "the office fridge is getting cleaned out friday at 4, save your sad little yogurts",
    "the cat knocked the monstera off the shelf, plant survived, pot did not",
    "our airbnb host says early check in is fine, code is 4482 on the side door",
    "the professor posted the rubric, the essay is worth 30 percent not 20",
    "I talked to the contractor, demo starts monday and they'll need the driveway clear",
    "the group chat voted, camping is the 12th through the 14th at pine flats",
    "the new hire starts wednesday, her name is elena and she's coming from stripe",
    "my knee held up fine on the long run, physio exercises are actually working",
    "the venue emailed back, they can do 80 people if we use the garden side too",
    "the compost bin has worms now which is either great or terrible, researching",
    "the bank flagged the card, it was just the hotel hold, all cleared up now",
    "the trailer for that heist movie dropped and it looks genuinely great",
    "our internet plan renews next month, the competitor is offering double speed for less",
    "aunt rosa is bringing the tamales so do not buy any appetizers",
    "the release notes are drafted, someone from support should sanity check the wording",
    "the piano tuner can come thursday morning, takes about an hour",
    "I maxed out the hsa contribution this paycheck so the deposit looks smaller than usual",
    "the farmers market moved indoors for the season, same saturday hours",
    "the toddler slept through the night twice in a row, we are afraid to speak of it",
    "the design system update ships behind a flag, nothing changes visually yet",
    "the umpire canceled the game for lightning, makeup date is next sunday",
    "my laptop battery is at 62 percent health so I booked the repair for friday",
    "the church bake sale wants two dozen anything, I'm doing the lemon bars",
    "the road by the school is closed for paving all week, use the maple street entrance",
    "the therapist had a cancellation so I got bumped up to tuesday at 5",
    "the drone footage from the lake trip came out incredible, sending the link tonight",
    "the hoa approved the fence color, we are officially a beige household now",
    "the intern's demo crashed twice but honestly the idea underneath is solid",
    "my fantasy team is somehow in the playoffs despite me forgetting three lineups",
    "the seed order shipped, the tomatoes and the weird striped beans are on the way",
    "the museum does free thursdays after 5 if we want a cheap date night",
]

# Sentences where a filler word is present and should be PRESERVED verbatim
# (restraint training: fillers are wording, not structure).
FILLER_KEEPERS = [
    "so um the meeting got pushed to 4 and I have a hard stop at 4:30",
    "it's uh honestly kind of a mess but the numbers are trending the right way",
    "I was like halfway through the form when the session timed out",
    "the apartment is um smaller than the photos but the light is unreal",
    "he said the timeline is you know flexible but I wouldn't test it",
    "the soup needs um maybe ten more minutes and a lot more salt",
]
