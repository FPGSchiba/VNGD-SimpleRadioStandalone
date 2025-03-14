# VNGD-SimpleRadioStandalone
An open source Stand alone Radio for Star Citizen used by the VNGD Organisation

Visit Vanguard at: https://vngd.net/

Download: Link: [Latest Release](https://github.com/FPGSchiba/VNGD-SimpleRadioStandalone/releases/latest)

## Contribution Guidelines

### General Workflow

Following Workflow is used within this Project:
 1. Developer creates a new Branch
 2. One commit at least determines the increase in Versioning
    1. For `patch` increase Commit without any syntax
    2. For `minor` increase add `[MINOR]` to one Commit message for your Pull Request (See Point: **4.**)
    3. For `major` increase add `[MAJOR]` to one Commit message for yor Pull Request (See Point: **4.**)
3. Commit your code with the guidelines above like you would
4. Create a Pull Request to merge into the `master` branch
5. Wait until a Contributor has Approved your Pull Request
6. Versioning will increase with the changes visible within your Commit history

If changes directly on the Repo or not specifically for a feature are to be done, please use the `develop` branch. And once you are done with the changes create a Pull Request (Same as normal Workflow)

If those changes are small and only adjust some configuration, those can be done directly on the `master` Branch, but please keep it to a minimum.

### Naming Convention

 1. Branches
    - Branches are prefixed with `/feature`, `/bugfix` or `/hotfix` depending on the use the branch serves.
    - Branch names should not be longer then 50 characters (including the prefix) and be as describing as possible.
2. Commits
    - Commits should have a 'header' message in which the changes are condensed in words within a 50 character limie
    - After the header there is a more complete message describing the changes done
    - For Example:
    > #63 | Added new Settings
    > 
    > \* Added functionality to have multiple settings for Client Window Positions
    >
    > \* Added Settings to accomodate resizing of the Main Window
3. Pull Requests
    - No specific Naming Convention

### Tips

- Try to always request a Approval for a Pull request
- If you want to link to issues in Commits use this syntax: `#{Issue-ID} | {commit}` Issue-ID beeing the Number representing the issue, for example: `#77 | Fixed issue`

## Tagging / Release Guidelines
Releases are named with [Semantic Versioning](https://devconnected.com/how-to-create-git-tags/#:~:text=In%20order%20to%20create%20a,that%20you%20want%20to%20create.&text=As%20an%20example%2C%20let's%20say,command%20and%20specify%20the%20tagname)

That means following features may change with each Verison:
* **major** : is a version number where you introduced breaking modifications (modifications of your new version are NOT compatible with previous versions);
* **minor** : is a version number that is compatible with previous versions;
* **patch** : is an increment for a bug fix or a patch fix done on your software.

Following is the Structure of a release name: `v{major}.{minor}.{patch}` for example: `v1.0.0` or `v2.2.1`

## Release History

### v2.0.0-alpha
- [MAJOR] UI Refactor