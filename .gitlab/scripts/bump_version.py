# Bumps <Version>/<AssemblyVersion>/<FileVersion> in Vistumbler.UI.csproj and rolls the "## master" section of
# CHANGELOG.md into a new version section. GitLab port of the script in .github/workflows/bump-version.yml.
#
# Usage: python .gitlab/scripts/bump_version.py <patch|minor|major|prepatch|preminor|premajor|prerelease> [preid]
# Writes the new version to bump_version.txt for the calling job.
import json, os, re, subprocess, sys, urllib.request

version_type   = sys.argv[1]
preid          = sys.argv[2] if len(sys.argv) > 2 and sys.argv[2].strip() else 'rc'
csproj_path    = 'Vistumbler.UI/Vistumbler.UI.csproj'
changelog_path = 'CHANGELOG.md'


# Files are UTF-8 (the csproj with a BOM) with CRLF line endings on the Windows runner. Read them as UTF-8
# (not the Windows ANSI default, which fails on the changelog's emoji) and write them back the same way.
def read_text(path):
    raw = open(path, 'rb').read()
    bom = raw.startswith(b'\xef\xbb\xbf')
    newline = '\r\n' if b'\r\n' in raw else '\n'
    return raw.decode('utf-8-sig').replace('\r\n', '\n'), bom, newline


def write_text(path, text, bom, newline):
    with open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline) as f:
        f.write(text)


# ── Read current version from .csproj ───────────────────────────────────
csproj, csproj_bom, csproj_nl = read_text(csproj_path)
m = re.search(r'<Version>([^<]+)</Version>', csproj)
if not m:
    raise RuntimeError(f"Could not find <Version> in {csproj_path}")
current = m.group(1).strip()
print(f"Current version: {current}")

# ── Parse version (supports X.Y.Z and X.Y.Z-pre.N) ──────────────────────
pre_match = re.match(r'^(\d+)\.(\d+)\.(\d+)-(.+?)\.(\d+)$', current)
rel_match = re.match(r'^(\d+)\.(\d+)\.(\d+)$', current)

if pre_match:
    major, minor, patch = int(pre_match.group(1)), int(pre_match.group(2)), int(pre_match.group(3))
    pre_tag, pre_num = pre_match.group(4), int(pre_match.group(5))
    is_pre = True
elif rel_match:
    major, minor, patch = int(rel_match.group(1)), int(rel_match.group(2)), int(rel_match.group(3))
    pre_tag, pre_num = None, 0
    is_pre = False
else:
    raise RuntimeError(f"Unrecognised version format: {current}")

# ── Compute new version ──────────────────────────────────────────────────
if version_type == 'patch':
    new_version = f"{major}.{minor}.{patch}" if is_pre else f"{major}.{minor}.{patch + 1}"
elif version_type == 'minor':
    new_version = f"{major}.{minor}.0" if is_pre else f"{major}.{minor + 1}.0"
elif version_type == 'major':
    new_version = f"{major}.0.0" if is_pre else f"{major + 1}.0.0"
elif version_type == 'prepatch':
    new_version = f"{major}.{minor}.{patch + 1}-{preid}.1"
elif version_type == 'preminor':
    new_version = f"{major}.{minor + 1}.0-{preid}.1"
elif version_type == 'premajor':
    new_version = f"{major + 1}.0.0-{preid}.1"
elif version_type == 'prerelease':
    if is_pre:
        new_version = f"{major}.{minor}.{patch}-{pre_tag}.{pre_num + 1}"
    else:
        new_version = f"{major}.{minor}.{patch + 1}-{preid}.1"
else:
    raise RuntimeError(f"Unknown version type: {version_type}")

print(f"New version: {new_version}")

# ── Update .csproj ───────────────────────────────────────────────────────
# Four-part AssemblyVersion/FileVersion only supports X.Y.Z.0
base = re.sub(r'-.*$', '', new_version)
parts = base.split('.')
while len(parts) < 3:
    parts.append('0')
four_part = '.'.join(parts[:3]) + '.0'

csproj = re.sub(r'<Version>[^<]+</Version>', f'<Version>{new_version}</Version>', csproj)
csproj = re.sub(r'<AssemblyVersion>[^<]+</AssemblyVersion>', f'<AssemblyVersion>{four_part}</AssemblyVersion>', csproj)
csproj = re.sub(r'<FileVersion>[^<]+</FileVersion>', f'<FileVersion>{four_part}</FileVersion>', csproj)
write_text(csproj_path, csproj, csproj_bom, csproj_nl)
print(f"Updated {csproj_path}")


def finish():
    open('bump_version.txt', 'w', encoding='utf-8').write(new_version)
    sys.exit(0)


# ── Update CHANGELOG.md ──────────────────────────────────────────────────
if not os.path.exists(changelog_path):
    print("No CHANGELOG.md found, skipping")
    finish()

changelog, changelog_bom, changelog_nl = read_text(changelog_path)

# Collect merge requests since the last tag that aren't already in the changelog
missing_entries = []
try:
    latest_tag = subprocess.check_output(['git', 'describe', '--tags', '--abbrev=0'], text=True).strip()
    print(f"Latest tag: {latest_tag}")
    commit_range = f"{latest_tag}..HEAD"
except subprocess.CalledProcessError:
    print("No previous tags found, using all commits")
    commit_range = 'HEAD'

try:
    # Full messages: GitLab merge commits reference the MR ("See merge request group/project!12") in the body
    commits = subprocess.check_output(['git', 'log', commit_range, '--format=%B'], text=True, encoding='utf-8')
except subprocess.CalledProcessError:
    commits = ''

mr_numbers = list(dict.fromkeys(re.findall(r'!(\d+)\b', commits)))
missing_mrs = [n for n in mr_numbers if f'!{n}' not in changelog]
print(f"Found {len(missing_mrs)} new merge requests to add to changelog")

api, project, token = os.environ.get('CI_API_V4_URL'), os.environ.get('CI_PROJECT_ID'), os.environ.get('CI_PUSH_TOKEN')
for iid in missing_mrs:
    try:
        req = urllib.request.Request(f"{api}/projects/{project}/merge_requests/{iid}", headers={'PRIVATE-TOKEN': token})
        mr = json.load(urllib.request.urlopen(req))
        author = mr['author']['username']
        if re.search(r'bot|dependabot|renovate', author, re.I):
            continue
        entry = f"- {mr['title']} ([!{mr['iid']}]({mr['web_url']})) (@{author})"
        missing_entries.append(entry)
        print(f"Added: {entry}")
    except Exception as e:
        print(f"Could not fetch merge request !{iid}: {e}")

# Replace "## master" with new version heading
changelog = changelog.replace('## master', f'## {new_version}', 1)
# Remove placeholder lines
changelog = changelog.replace('- _...Add new stuff here..._\n', '')

# Prepend fresh master section
master_section = '\n'.join([
    '## master',
    '### ✨ Features and improvements',
    '- _...Add new stuff here..._',
    '',
    '### 🐞 Bug fixes',
    '- _...Add new stuff here..._',
    '',
    '',
])

title_match = re.match(r'^(# .+?\n\n)', changelog)
if title_match:
    title = title_match.group(1)
    rest = changelog[len(title):]
else:
    title = ''
    rest = changelog

# Insert missing MR entries into the Bug fixes block of the new version section
if missing_entries:
    bug_fix_pattern = re.compile(
        r'^(## [^\n]+\n### ✨ Features and improvements\n(?:.*\n)*?### 🐞 Bug fixes\n)',
        re.MULTILINE)
    bf_match = bug_fix_pattern.search(rest)
    if bf_match:
        insert_at = bf_match.end()
        entries_text = '\n' + '\n'.join(missing_entries) + '\n'
        rest = rest[:insert_at] + entries_text + rest[insert_at:]
    else:
        rest = rest + '\n' + '\n'.join(missing_entries) + '\n'

changelog = title + master_section + rest
write_text(changelog_path, changelog, changelog_bom, changelog_nl)
print("Updated CHANGELOG.md")
finish()
