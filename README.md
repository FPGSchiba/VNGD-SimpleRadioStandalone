# VNGD-SimpleRadioStandalone
An open source Stand alone Radio for Star Citizen used by the VNGD Organisation

Visit Vanguard at: https://vngd.net/

Download: Link: [Latest Release](https://github.com/FPGSchiba/VNGD-SimpleRadioStandalone/releases/latest)

## Contribution Guidelines
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

## Tagging / Release Guidelines
Releases are named with [Semantic Versioning](https://devconnected.com/how-to-create-git-tags/#:~:text=In%20order%20to%20create%20a,that%20you%20want%20to%20create.&text=As%20an%20example%2C%20let's%20say,command%20and%20specify%20the%20tagname)

That means following features may change with each Verison:
* **major** : is a version number where you introduced breaking modifications (modifications of your new version are NOT compatible with previous versions);
* **minor** : is a version number that is compatible with previous versions;
* **patch** : is an increment for a bug fix or a patch fix done on your software.

Following is the Structure of a release name: `v{major}.{minor}.{patch}` for example: `v1.0.0` or `v2.2.1`

## ToDo
* Remove this section again :D